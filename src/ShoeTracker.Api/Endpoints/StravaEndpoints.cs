using System.Security.Claims;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services.Geocoding;
using ShoeTracker.Api.Services.Strava;

namespace ShoeTracker.Api.Endpoints;

public static class StravaEndpoints
{
    public const string StateCookieName = "ShoeTracker.StravaState";

    public static void MapStravaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/strava").RequireAuthorization();

        // Full-page navigation from the client, not a fetch: ends with a redirect to Strava.
        group.MapGet("/connect", (HttpContext http, StravaClient strava, IOptions<StravaOptions> options, IHostEnvironment env) =>
        {
            if (!options.Value.IsConfigured)
            {
                return Results.Problem("Strava isn't configured on this server.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Ties the callback to this browser, so a callback someone else started can't
            // attach their Strava account to this user.
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            http.Response.Cookies.Append(StateCookieName, state, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                // Same rule as the auth cookie: behind Caddy the API itself only ever sees HTTP.
                Secure = !env.IsDevelopment(),
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/"
            });

            return Results.Redirect(strava.AuthorizeUrl(state));
        });

        // Strava redirects the browser here. Every outcome redirects back into the app with a
        // ?strava=<result> flag for the client to show.
        group.MapGet("/callback", async (string? code, string? state, string? scope, string? error,
            HttpContext http, ClaimsPrincipal user, StravaClient strava, StravaTokenStore tokens, ILogger<StravaClient> logger) =>
        {
            var expectedState = http.Request.Cookies[StateCookieName];
            http.Response.Cookies.Delete(StateCookieName, new CookieOptions { Path = "/" });

            if (expectedState is null || state is null
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
            {
                return BackToApp("error");
            }

            if (error is not null) return BackToApp("denied");
            if (string.IsNullOrEmpty(code)) return BackToApp("error");

            var grantedScopes = (scope ?? "").Split(',', StringSplitOptions.TrimEntries);
            if (!grantedScopes.Contains(StravaClient.RequiredScope)) return BackToApp("missing-scope");

            StravaTokenResponse tokenResponse;
            try
            {
                tokenResponse = await strava.ExchangeCodeAsync(code);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Strava token exchange failed.");
                return BackToApp("error");
            }

            await tokens.SaveAsync(user.GetUserId(), tokenResponse, scope!);
            return BackToApp("connected");
        });

        group.MapGet("/status", async (ClaimsPrincipal user, StravaTokenStore tokens, IOptions<StravaOptions> options, ShoeTrackerContext db) =>
        {
            var userId = user.GetUserId();
            var connection = await tokens.GetConnectionAsync(userId);
            var importedRuns = await db.Runs.CountAsync(r => r.UserId == userId && r.Source == RunSource.Strava);
            return Results.Ok(new StravaStatusResponse(options.Value.IsConfigured, connection is not null, connection?.AthleteName, importedRuns));
        });

        group.MapPost("/import", async (ClaimsPrincipal user, StravaImporter importer, PlaceNameSignal placeNames, ILogger<StravaImporter> logger) =>
        {
            try
            {
                var imported = await importer.ImportAsync(user.GetUserId());
                // New start points may need naming; look them up in the background.
                if (imported is not null) placeNames.Notify();
                return imported is null
                    ? Results.Problem("Strava isn't connected.", statusCode: StatusCodes.Status409Conflict)
                    : Results.Ok(new StravaImportResponse(imported.Value));
            }
            catch (DbUpdateException)
            {
                // Another import for this user saved the same activities first (unique index).
                return Results.Problem("Another Strava import is already running.", statusCode: StatusCodes.Status409Conflict);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Results.Problem("Strava's rate limit was reached. Try again in 15 minutes.", statusCode: StatusCodes.Status429TooManyRequests);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Strava import failed.");
                return Results.Problem("Strava couldn't be reached, or rejected the request. Try again, or disconnect and reconnect Strava.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapDelete("/connection", async (ClaimsPrincipal user, StravaClient strava, StravaTokenStore tokens, ILogger<StravaClient> logger) =>
        {
            var userId = user.GetUserId();
            var connection = await tokens.GetConnectionAsync(userId);
            if (connection is null) return Results.NoContent();

            // Revoking on Strava's side is best-effort: the local tokens are deleted either way,
            // and the user can also revoke the app from their Strava settings.
            try
            {
                var accessToken = await tokens.GetValidAccessTokenAsync(userId);
                if (accessToken is not null) await strava.DeauthorizeAsync(accessToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Strava deauthorization failed; deleting the local connection anyway.");
            }

            await tokens.DeleteAsync(connection);
            return Results.NoContent();
        });
    }

    // Relative to the site root: the browser is on <origin>/api/strava/callback, so "/" is the app.
    private static IResult BackToApp(string result) => Results.Redirect($"/?strava={result}");
}
