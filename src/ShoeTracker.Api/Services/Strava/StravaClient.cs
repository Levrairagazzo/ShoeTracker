using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>Typed HttpClient for Strava's OAuth endpoints.</summary>
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
