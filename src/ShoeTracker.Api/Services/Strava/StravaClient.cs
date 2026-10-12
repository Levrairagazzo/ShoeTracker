using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>Typed HttpClient for Strava's OAuth endpoints, the activities API and webhook subscriptions.</summary>
public class StravaClient(HttpClient http, IOptions<StravaOptions> options)
{
    public static readonly Uri BaseAddress = new("https://www.strava.com/");

    public const string RequiredScope = "activity:read_all";

    public string AuthorizeUrl(string state)
    {
        var o = options.Value;
        var query = string.Join("&",
            $"client_id={Uri.EscapeDataString(o.ClientId!)}",
            $"redirect_uri={Uri.EscapeDataString(o.RedirectUri!)}",
            "response_type=code",
            "approval_prompt=auto",
            $"scope={Uri.EscapeDataString(RequiredScope)}",
            $"state={Uri.EscapeDataString(state)}");
        return new Uri(BaseAddress, $"oauth/authorize?{query}").ToString();
    }

    public Task<StravaTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["grant_type"] = "authorization_code"
        }, ct);

    public Task<StravaTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, ct);

    public async Task DeauthorizeAsync(string accessToken, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = accessToken });
        using var response = await http.PostAsync("oauth/deauthorize", content, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>One page of the athlete's activities, newest first, optionally only those started after <paramref name="after"/>.</summary>
    public async Task<IReadOnlyList<StravaActivity>> GetActivitiesAsync(
        string accessToken, DateTimeOffset? after, int page, int perPage, CancellationToken ct = default)
    {
        var query = $"api/v3/athlete/activities?page={page}&per_page={perPage}";
        if (after is { } since) query += $"&after={since.ToUnixTimeSeconds()}";

        using var response = await SendAuthorizedAsync(HttpMethod.Get, query, accessToken, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<StravaActivity>>(ct) ?? [];
    }

    /// <summary>One activity, or null if Strava doesn't have it (e.g. it was deleted).</summary>
    public async Task<StravaActivity?> GetActivityAsync(string accessToken, long activityId, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, $"api/v3/activities/{activityId}", accessToken, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StravaActivity>(ct);
    }

    /// <summary>The authenticated athlete; fails with 401 once they've revoked the app's access.</summary>
    public async Task<StravaAthlete> GetAthleteAsync(string accessToken, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, "api/v3/athlete", accessToken, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StravaAthlete>(ct)
            ?? throw new InvalidOperationException("Strava returned an empty athlete.");
    }

    /// <summary>The app's webhook subscriptions (Strava allows at most one).</summary>
    public async Task<IReadOnlyList<StravaPushSubscription>> GetPushSubscriptionsAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var query = $"api/v3/push_subscriptions?client_id={Uri.EscapeDataString(o.ClientId!)}&client_secret={Uri.EscapeDataString(o.ClientSecret!)}";
        using var response = await http.GetAsync(query, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<StravaPushSubscription>>(ct) ?? [];
    }

    /// <summary>
    /// Subscribes the app to webhook events. Strava validates <paramref name="callbackUrl"/> before
    /// answering, by sending it <paramref name="verifyToken"/>, so the callback must already be live.
    /// </summary>
    public async Task<long> CreatePushSubscriptionAsync(string callbackUrl, string verifyToken, CancellationToken ct = default)
    {
        var o = options.Value;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = o.ClientId!,
            ["client_secret"] = o.ClientSecret!,
            ["callback_url"] = callbackUrl,
            ["verify_token"] = verifyToken
        });
        using var response = await http.PostAsync("api/v3/push_subscriptions", content, ct);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<StravaPushSubscription>(ct)
            ?? throw new InvalidOperationException("Strava returned an empty subscription.");
        return created.Id;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await http.SendAsync(request, ct);
    }

    private async Task<StravaTokenResponse> PostTokenAsync(Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["client_id"] = options.Value.ClientId!;
        fields["client_secret"] = options.Value.ClientSecret!;

        using var content = new FormUrlEncodedContent(fields);
        using var response = await http.PostAsync("oauth/token", content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StravaTokenResponse>(ct)
            ?? throw new InvalidOperationException("Strava returned an empty token response.");
    }
}

public record StravaTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at")] long ExpiresAt,
    [property: JsonPropertyName("athlete")] StravaAthlete? Athlete);

public record StravaAthlete(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("firstname")] string? FirstName,
    [property: JsonPropertyName("lastname")] string? LastName);

/// <param name="Distance">In metres.</param>
/// <param name="StartDateLocal">Wall-clock start time where the activity happened (Strava marks it "Z", but it isn't UTC).</param>
/// <param name="Trainer">Recorded indoors, e.g. a treadmill run logged as a plain "Run".</param>
/// <param name="WorkoutType">For runs: 0 default, 1 race, 2 long run, 3 workout.</param>
/// <param name="StartLatLng">[latitude, longitude], or empty for activities without GPS.</param>
/// <param name="Athlete">The activity's owner.</param>
public record StravaActivity(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("sport_type")] string? SportType,
    [property: JsonPropertyName("distance")] double Distance,
    [property: JsonPropertyName("start_date_local")] DateTime StartDateLocal,
    [property: JsonPropertyName("trainer")] bool Trainer = false,
    [property: JsonPropertyName("workout_type")] int? WorkoutType = null,
    [property: JsonPropertyName("start_latlng")] double[]? StartLatLng = null,
    [property: JsonPropertyName("athlete")] StravaActivityOwner? Athlete = null);

public record StravaActivityOwner([property: JsonPropertyName("id")] long Id);

public record StravaPushSubscription(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("callback_url")] string? CallbackUrl = null);
