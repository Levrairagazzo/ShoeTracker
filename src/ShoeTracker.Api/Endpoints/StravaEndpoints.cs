using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ShoeTracker.Api.Dtos;
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

        group.MapGet("/status", async (ClaimsPrincipal user, StravaTokenStore tokens, IOptions<StravaOptions> options) =>
        {
            var connection = await tokens.GetConnectionAsync(user.GetUserId());
            return Results.Ok(new StravaStatusResponse(options.Value.IsConfigured, connection is not null, connection?.AthleteName));
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
