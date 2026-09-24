using System.Globalization;
using System.Text.Json.Serialization;

namespace ShoeTracker.Api.Services.Geocoding;

/// <summary>
/// Typed HttpClient for OpenStreetMap's Nominatim reverse geocoder. Its usage policy requires an
/// identifying User-Agent, at most one request per second, and caching results; the caller
/// (<see cref="PlaceNameResolver"/>) handles the last two.
/// </summary>
public class NominatimClient(HttpClient http)
{
    public static readonly Uri BaseAddress = new("https://nominatim.openstreetmap.org/");

    public const string UserAgent = "ShoeTracker/1.0 (+https://milesleft.run)";

    /// <returns>A place name like "Oakland, California", or an empty string if there's no named place there.</returns>
    public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var query = string.Create(CultureInfo.InvariantCulture,
            $"reverse?format=jsonv2&lat={latitude}&lon={longitude}&zoom=10&accept-language=en");
        var result = await http.GetFromJsonAsync<ReverseResult>(query, ct);
        return result?.Address is { } address ? FormatName(address) : "";
    }

    /// <summary>Town plus state for the US ("Oakland, California"), town plus country elsewhere ("Golden, Canada").</summary>
    public static string FormatName(Address address)
    {
        var place = address.City ?? address.Town ?? address.Village ?? address.Hamlet ?? address.Municipality ?? address.County;
        var region = address.CountryCode == "us" ? address.State : address.Country;
        return string.Join(", ", new[] { place, region }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private record ReverseResult([property: JsonPropertyName("address")] Address? Address);

    public record Address(
        [property: JsonPropertyName("city")] string? City = null,
        [property: JsonPropertyName("town")] string? Town = null,
        [property: JsonPropertyName("village")] string? Village = null,
        [property: JsonPropertyName("hamlet")] string? Hamlet = null,
        [property: JsonPropertyName("municipality")] string? Municipality = null,
        [property: JsonPropertyName("county")] string? County = null,
        [property: JsonPropertyName("state")] string? State = null,
        [property: JsonPropertyName("country")] string? Country = null,
        [property: JsonPropertyName("country_code")] string? CountryCode = null);
}
