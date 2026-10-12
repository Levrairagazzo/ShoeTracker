using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>Typed HttpClient for Strava's OAuth endpoints and the activities API.</summary>
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

        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<StravaActivity>>(ct) ?? [];
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
public record StravaActivity(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("sport_type")] string? SportType,
    [property: JsonPropertyName("distance")] double Distance,
    [property: JsonPropertyName("start_date_local")] DateTime StartDateLocal,
    [property: JsonPropertyName("trainer")] bool Trainer = false,
    [property: JsonPropertyName("workout_type")] int? WorkoutType = null,
    [property: JsonPropertyName("start_latlng")] double[]? StartLatLng = null);
