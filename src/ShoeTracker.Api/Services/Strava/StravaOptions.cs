namespace ShoeTracker.Api.Services.Strava;

/// <summary>Strava API app credentials, bound from the "Strava" configuration section.</summary>
public class StravaOptions
{
    public const string SectionName = "Strava";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>
    /// Absolute URL of <c>/strava/callback</c> as the browser sees it, i.e. including the
    /// <c>/api</c> prefix the proxy strips. Its domain must match the Strava app's callback domain.
    /// </summary>
    public string? RedirectUri { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri);
}
