using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>
/// Imports a user's Strava runs as <see cref="Run"/>s. Safe to re-run: activities already
/// imported (by <see cref="Run.StravaActivityId"/>) are updated in place, not duplicated, and
/// keep whatever shoe they've been assigned to.
/// </summary>
public class StravaImporter(ShoeTrackerContext db, StravaClient strava, StravaTokenStore tokens, TrainingLog log)
{
    public const int PageSize = 200;

    public const int RaceWorkoutType = 1;

    /// <summary>Strava sport types that count as runs. VirtualRun is a treadmill run.</summary>
    public static readonly IReadOnlySet<string> RunSportTypes = new HashSet<string> { "Run", "TrailRun", "VirtualRun" };

    /// <returns>The number of newly imported runs, or null if the user hasn't connected Strava.</returns>
    public async Task<int?> ImportAsync(int userId, CancellationToken ct = default)
    {
        var connection = await tokens.GetConnectionAsync(userId, ct);
        var accessToken = await tokens.GetValidAccessTokenAsync(userId, ct);
        if (connection is null || accessToken is null) return null;

        var existing = await db.Runs
            .Where(r => r.UserId == userId && r.StravaActivityId != null)
            .ToDictionaryAsync(r => r.StravaActivityId!.Value, ct);

        var after = IncrementalStart(existing.Values);

        // The first import after connecting is the user's history, which stays unassigned (E2
        // estimates it); runs arriving after that are new and go to the default shoe, if one is set.
        var isFirstImport = connection.HistoryImportedAt is null;
        var shoeForNewRuns = isFirstImport ? null : await log.ShoeForNewStravaRunAsync(userId, ct);

        var imported = 0;
        for (var page = 1; ; page++)
        {
            var activities = await strava.GetActivitiesAsync(accessToken, after, page, PageSize, ct);

            foreach (var activity in activities)
            {
                if (!IsRun(activity) || activity.Distance <= 0) continue;

                if (!existing.TryGetValue(activity.Id, out var run))
                {
                    run = new Run { UserId = userId, ShoeId = shoeForNewRuns, Source = RunSource.Strava, StravaActivityId = activity.Id };
                    db.Runs.Add(run);
                    existing[activity.Id] = run;
                    imported++;
                }

                // Strava owns its runs, so an already-imported one takes Strava's current values.
                Apply(activity, run);
            }

            if (activities.Count < PageSize) break;
        }

        connection.HistoryImportedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return imported;
    }

    /// <summary>
    /// Brings one activity's run in line with Strava (used for webhook events, which only say
    /// that something changed): adds it if it's a new run, updates it, or removes it if it's been
    /// deleted or is no longer a run. Ignored until the user's history has been imported.
    /// </summary>
    public async Task<ActivitySyncResult> SyncActivityAsync(int userId, long activityId, CancellationToken ct = default)
    {
        var connection = await tokens.GetConnectionAsync(userId, ct);
        if (connection?.HistoryImportedAt is null) return ActivitySyncResult.Ignored;

        var accessToken = await tokens.GetValidAccessTokenAsync(userId, ct);
        if (accessToken is null) return ActivitySyncResult.Ignored;

        var activity = await strava.GetActivityAsync(accessToken, activityId, ct);
        // Strava shows other athletes' public activities too; only the user's own count.
        if (activity is not null && activity.Athlete?.Id != connection.AthleteId) return ActivitySyncResult.Ignored;

        var run = await db.Runs.SingleOrDefaultAsync(r => r.UserId == userId && r.StravaActivityId == activityId, ct);
        ActivitySyncResult result;
        if (activity is null || !IsRun(activity) || activity.Distance <= 0)
        {
            if (run is null) return ActivitySyncResult.Ignored;
            db.Runs.Remove(run);
            result = ActivitySyncResult.Removed;
        }
        else
        {
            result = ActivitySyncResult.Updated;
            if (run is null)
            {
                run = new Run { UserId = userId, ShoeId = await log.ShoeForNewStravaRunAsync(userId, ct), Source = RunSource.Strava, StravaActivityId = activityId };
                db.Runs.Add(run);
                result = ActivitySyncResult.Added;
            }
            Apply(activity, run);
        }

        await db.SaveChangesAsync(ct);
        return result;
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

public enum ActivitySyncResult
{
    Ignored,
    Added,
    Updated,
    Removed
}
