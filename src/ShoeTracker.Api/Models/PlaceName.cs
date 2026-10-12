namespace ShoeTracker.Api.Models;

/// <summary>
/// Cached reverse-geocoding result for one ~1 km area (see <c>PlaceNameResolver.AreaKey</c>),
/// shared by every run that starts there.
/// </summary>
public class PlaceName
{
    public int Id { get; set; }

    public string AreaKey { get; set; } = string.Empty;

    /// <summary>e.g. "Oakland, California". Empty when the lookup found no named place.</summary>
    public string Name { get; set; } = string.Empty;
}
