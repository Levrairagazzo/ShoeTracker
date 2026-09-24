using System.Net;
using System.Net.Http.Json;

namespace ShoeTracker.Api.Tests;

/// <summary>
/// Stands in for strava.com in every test app, so tests never reach the real API. Records each
/// request and answers with <see cref="Respond"/>; unexpected calls get a 500 by default.
/// </summary>
public class FakeStravaHandler : HttpMessageHandler
{
    public record RecordedRequest(HttpMethod Method, string Path, IReadOnlyDictionary<string, string> Form);

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

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = request.Content is null
            ? new Dictionary<string, string>()
            : (await request.Content.ReadAsStringAsync(cancellationToken))
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv.ElementAtOrDefault(1) ?? ""));

        var recorded = new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, form);
        lock (Requests) Requests.Add(recorded);
        return Respond(recorded);
    }
}
