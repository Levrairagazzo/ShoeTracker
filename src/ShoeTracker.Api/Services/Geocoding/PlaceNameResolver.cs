using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Services.Geocoding;

/// <summary>
/// Names the places runs start from. Start points are grouped into ~1 km areas (coordinates
/// rounded to 2 decimals); each area is looked up once and cached in <see cref="PlaceName"/>.
/// Only the rounded area centre is ever sent to the geocoder, never a run's exact start point.
/// </summary>
public class PlaceNameResolver(
    ShoeTrackerContext db,
    NominatimClient nominatim,
    IOptions<GeocodingOptions> options,
    ILogger<PlaceNameResolver> logger)
{
    public static string AreaKey(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(latitude, 2):F2},{Math.Round(longitude, 2):F2}");

    private static string? AreaKey(Run run) =>
        run is { StartLatitude: { } lat, StartLongitude: { } lng } ? AreaKey(lat, lng) : null;

    /// <summary>Place names for the given runs, keyed by run id. Runs without a (resolved) name are left out.</summary>
    public async Task<IReadOnlyDictionary<int, string>> NamesForAsync(IEnumerable<Run> runs, CancellationToken ct = default)
    {
        var keyByRun = runs
            .Select(r => (r.Id, Key: AreaKey(r)))
            .Where(x => x.Key is not null)
            .ToDictionary(x => x.Id, x => x.Key!);
        if (keyByRun.Count == 0) return new Dictionary<int, string>();

        var keys = keyByRun.Values.Distinct().ToList();
        var names = await db.PlaceNames
            .Where(p => keys.Contains(p.AreaKey) && p.Name != "")
            .ToDictionaryAsync(p => p.AreaKey, p => p.Name, ct);

        return keyByRun
            .Where(x => names.ContainsKey(x.Value))
            .ToDictionary(x => x.Key, x => names[x.Value]);
    }

    /// <summary>
    /// Looks up every area that runs start in but that isn't cached yet, pausing between lookups
    /// to respect the geocoder's rate limit. Stops at the first failed lookup; the rest are
    /// retried on the next pass.
    /// </summary>
    /// <returns>How many areas were resolved.</returns>
    public async Task<int> ResolvePendingAsync(CancellationToken ct = default)
    {
        var starts = await db.Runs
            .Where(r => r.StartLatitude != null && r.StartLongitude != null)
            .Select(r => new { Lat = r.StartLatitude!.Value, Lng = r.StartLongitude!.Value })
            .ToListAsync(ct);
        var known = (await db.PlaceNames.Select(p => p.AreaKey).ToListAsync(ct)).ToHashSet();
        var pending = starts
            .Select(s => (Lat: Math.Round(s.Lat, 2), Lng: Math.Round(s.Lng, 2)))
            .Distinct()
            .Where(area => !known.Contains(AreaKey(area.Lat, area.Lng)))
            .ToList();

        var resolved = 0;
        foreach (var (lat, lng) in pending)
        {
            if (resolved > 0) await Task.Delay(options.Value.MinInterval, ct);

            string name;
            try
            {
                name = await nominatim.ReverseAsync(lat, lng, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Place-name lookup failed; {Remaining} areas left for the next pass.", pending.Count - resolved);
                break;
            }

            db.PlaceNames.Add(new PlaceName { AreaKey = AreaKey(lat, lng), Name = name });
            await db.SaveChangesAsync(ct);
            resolved++;
        }

        return resolved;
    }
}
