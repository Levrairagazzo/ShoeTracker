using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>
/// Acts on one Strava webhook event. Events aren't signed, so they're treated only as hints:
/// what actually changes is decided by asking Strava, with the athlete's own token.
/// </summary>
public class StravaWebhookHandler(
    ShoeTrackerContext db,
    StravaImporter importer,
    StravaClient strava,
    StravaTokenStore tokens,
    ILogger<StravaWebhookHandler> logger)
{
    /// <returns>True if a run was added or updated (so its start point may need naming).</returns>
    public async Task<bool> HandleAsync(StravaWebhookEvent webhookEvent, CancellationToken ct = default)
    {
        var userIds = await db.StravaConnections
            .Where(c => c.AthleteId == webhookEvent.OwnerId)
            .Select(c => c.UserId)
            .ToListAsync(ct);

        var runsChanged = false;
        foreach (var userId in userIds)
        {
            switch (webhookEvent.ObjectType)
            {
                case "activity":
                    var result = await importer.SyncActivityAsync(userId, webhookEvent.ObjectId, ct);
                    runsChanged |= result is ActivitySyncResult.Added or ActivitySyncResult.Updated;
                    break;
                case "athlete" when IsDeauthorization(webhookEvent):
                    await DisconnectIfRevokedAsync(userId, ct);
                    break;
            }
        }
        return runsChanged;
    }

    private static bool IsDeauthorization(StravaWebhookEvent webhookEvent) =>
        webhookEvent.Updates is { } updates
        && updates.TryGetValue("authorized", out var authorized)
        && authorized.ValueKind is JsonValueKind.String or JsonValueKind.False
        && string.Equals(authorized.ToString(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The user revoked the app on Strava's side. Deletes the stored connection once Strava
    /// confirms the tokens no longer work; imported runs are kept, as with disconnecting in the app.
    /// </summary>
    private async Task DisconnectIfRevokedAsync(int userId, CancellationToken ct)
    {
        var connection = await tokens.GetConnectionAsync(userId, ct);
        if (connection is null) return;

        try
        {
            var accessToken = await tokens.GetValidAccessTokenAsync(userId, ct);
            if (accessToken is not null) await strava.GetAthleteAsync(accessToken, ct);
            return;
        }
        // 401 from the API, or 400 from refreshing a revoked refresh token.
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
        }

        await tokens.DeleteAsync(connection, ct);
        logger.LogInformation("User {UserId} revoked Strava access; removed their connection.", userId);
    }
}
