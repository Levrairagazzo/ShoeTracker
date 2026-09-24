namespace ShoeTracker.Api.Services.Geocoding;

/// <summary>Reverse-geocoding settings, bound from the "Geocoding" configuration section.</summary>
public class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    /// <summary>Runs the background place-name lookups. Off in tests, which call the resolver directly.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gap between lookups. Nominatim's usage policy allows at most one request per second.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromSeconds(1.1);
}
