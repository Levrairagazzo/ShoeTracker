using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>
/// Owns reading and writing a user's Strava connection, so tokens are only ever handled
/// encrypted at rest and refreshed before they expire.
/// </summary>
public class StravaTokenStore(ShoeTrackerContext db, StravaClient strava, IDataProtectionProvider dataProtection)
{
    /// <summary>Refresh this long before Strava's expiry, so a token can't expire mid-request.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("ShoeTracker.StravaTokens");

    public Task<StravaConnection?> GetConnectionAsync(int userId, CancellationToken ct = default) =>
        db.StravaConnections.FirstOrDefaultAsync(c => c.UserId == userId, ct);

    public async Task SaveAsync(int userId, StravaTokenResponse tokens, string scope, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(userId, ct);
        if (connection is null)
        {
            connection = new StravaConnection { UserId = userId };
            db.StravaConnections.Add(connection);
        }

        if (tokens.Athlete is { } athlete)
        {
            connection.AthleteId = athlete.Id;
            connection.AthleteName = $"{athlete.FirstName} {athlete.LastName}".Trim();
        }

        connection.Scope = scope;
        connection.ConnectedAt = DateTimeOffset.UtcNow;
        SetTokens(connection, tokens);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns a usable access token for the user, refreshing it first if it's about to expire,
    /// or null if the user hasn't connected Strava.
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync(int userId, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(userId, ct);
        if (connection is null) return null;

        if (connection.ExpiresAt > DateTimeOffset.UtcNow + RefreshMargin)
        {
            return _protector.Unprotect(connection.EncryptedAccessToken);
        }

        var refreshed = await strava.RefreshAsync(_protector.Unprotect(connection.EncryptedRefreshToken), ct);
        SetTokens(connection, refreshed);
        await db.SaveChangesAsync(ct);
        return refreshed.AccessToken;
    }

    public async Task DeleteAsync(StravaConnection connection, CancellationToken ct = default)
    {
        db.StravaConnections.Remove(connection);
        await db.SaveChangesAsync(ct);
    }

    private void SetTokens(StravaConnection connection, StravaTokenResponse tokens)
    {
        connection.EncryptedAccessToken = _protector.Protect(tokens.AccessToken);
        // Strava may rotate the refresh token on every refresh; always keep the latest one.
        connection.EncryptedRefreshToken = _protector.Protect(tokens.RefreshToken);
        connection.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(tokens.ExpiresAt);
    }
}
