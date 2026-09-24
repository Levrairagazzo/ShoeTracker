using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>
/// Imports a user's Strava runs as unassigned <see cref="Run"/>s. Safe to re-run: activities
/// already imported (by <see cref="Run.StravaActivityId"/>) are updated in place, not duplicated.
/// </summary>
public class StravaImporter(ShoeTrackerContext db, StravaClient strava, StravaTokenStore tokens)
{
    public const int PageSize = 200;

    public const int RaceWorkoutType = 1;

    /// <summary>Strava sport types that count as runs. VirtualRun is a treadmill run.</summary>
    public static readonly IReadOnlySet<string> RunSportTypes = new HashSet<string> { "Run", "TrailRun", "VirtualRun" };

    /// <returns>The number of newly imported runs, or null if the user hasn't connected Strava.</returns>
    public async Task<int?> ImportAsync(int userId, CancellationToken ct = default)
    {
        var accessToken = await tokens.GetValidAccessTokenAsync(userId, ct);
        if (accessToken is null) return null;

        var existing = await db.Runs
            .Where(r => r.UserId == userId && r.StravaActivityId != null)
            .ToDictionaryAsync(r => r.StravaActivityId!.Value, ct);

        var after = IncrementalStart(existing.Values);

        var imported = 0;
        for (var page = 1; ; page++)
        {
            var activities = await strava.GetActivitiesAsync(accessToken, after, page, PageSize, ct);

            foreach (var activity in activities)
            {
                if (!IsRun(activity) || activity.Distance <= 0) continue;

                if (!existing.TryGetValue(activity.Id, out var run))
                {
                    run = new Run { UserId = userId, ShoeId = null, Source = RunSource.Strava, StravaActivityId = activity.Id };
                    db.Runs.Add(run);
                    existing[activity.Id] = run;
                    imported++;
                }

                // Strava owns its runs, so an already-imported one takes Strava's current values.
                Apply(activity, run);
            }

            if (activities.Count < PageSize) break;
        }

        await db.SaveChangesAsync(ct);
        return imported;
    }

    /// <summary>
    /// Only ask for activities from the day before the latest imported run. Dates are local to
    /// the run, so the extra day covers any time-zone offset. Returns null (fetch everything)
    /// for a first import, or while any imported run predates the fields added later
    /// (<see cref="Run.Type"/> is null), so a full fetch backfills them.
    /// </summary>
    private static DateTimeOffset? IncrementalStart(ICollection<Run> imported)
    {
        if (imported.Count == 0 || imported.Any(r => r.Type is null)) return null;

        var latest = imported.Max(r => r.Date);
        return new DateTimeOffset(latest.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static void Apply(StravaActivity activity, Run run)
    {
        run.Date = DateOnly.FromDateTime(activity.StartDateLocal);
        run.DistanceKm = Math.Round(activity.Distance / 1000, 3);
        run.Type = TypeOf(activity);
        run.IsRace = activity.WorkoutType == RaceWorkoutType;
        (run.StartLatitude, run.StartLongitude) = activity.StartLatLng is [var lat, var lng] ? (lat, lng) : ((double?)null, (double?)null);
    }

    private static string SportType(StravaActivity activity) => activity.SportType ?? activity.Type ?? "";

    private static bool IsRun(StravaActivity activity) => RunSportTypes.Contains(SportType(activity));

    private static RunType TypeOf(StravaActivity activity) => SportType(activity) switch
    {
        "VirtualRun" => RunType.Treadmill,
        _ when activity.Trainer => RunType.Treadmill,
        "TrailRun" => RunType.Trail,
        _ => RunType.Run
    };
}
