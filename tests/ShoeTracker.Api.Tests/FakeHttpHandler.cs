using System.Net;
using System.Net.Http.Json;

namespace ShoeTracker.Api.Tests;

/// <summary>
/// Stands in for an external HTTP API (Strava, the geocoder) in every test app, so tests never
/// reach the real services. Records each request and answers with <see cref="Respond"/>;
/// unexpected calls get a 500 by default. The static helpers build Strava-shaped responses.
/// </summary>
public class FakeHttpHandler : HttpMessageHandler
{
    public record RecordedRequest(
        HttpMethod Method,
        string Path,
        IReadOnlyDictionary<string, string> Query,
        IReadOnlyDictionary<string, string> Form,
        string? Authorization,
        string? UserAgent);

    public List<RecordedRequest> Requests { get; } = [];

    public Func<RecordedRequest, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    public static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    public static object TokenBody(string accessToken, string refreshToken, DateTimeOffset expiresAt, bool withAthlete = true) => new
    {
        token_type = "Bearer",
        access_token = accessToken,
        refresh_token = refreshToken,
        expires_at = expiresAt.ToUnixTimeSeconds(),
        athlete = withAthlete ? new { id = 12345L, firstname = "Test", lastname = "Runner" } : null
    };

    /// <summary>A Strava activity as <c>/athlete/activities</c> returns it (only the fields the app reads).</summary>
    public static object Activity(
        long id,
        double distanceMetres,
        string startDateLocal,
        string sportType = "Run",
        bool trainer = false,
        int? workoutType = null,
        double[]? startLatLng = null) => new
    {
        id,
        type = sportType,
        sport_type = sportType,
        distance = distanceMetres,
        start_date_local = startDateLocal,
        trainer,
        workout_type = workoutType,
        start_latlng = startLatLng ?? []
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = request.Content is null
            ? new Dictionary<string, string>()
            : (await request.Content.ReadAsStringAsync(cancellationToken))
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv.ElementAtOrDefault(1) ?? ""));

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        var recorded = new RecordedRequest(request.Method, request.RequestUri.AbsolutePath, query, form,
            request.Headers.Authorization?.ToString(), request.Headers.UserAgent.ToString());
        lock (Requests) Requests.Add(recorded);
        return Respond(recorded);
    }
}
