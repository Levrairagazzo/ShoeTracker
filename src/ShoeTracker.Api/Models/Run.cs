namespace ShoeTracker.Api.Models;

public class Run
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public double DistanceKm { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The shoe the run was done in, or null while the run is unassigned.</summary>
    public int? ShoeId { get; set; }

    public Shoe? Shoe { get; set; }

    public RunSource Source { get; set; } = RunSource.Manual;

    /// <summary>The Strava activity this run was imported from; null for manual runs.</summary>
    public long? StravaActivityId { get; set; }

    /// <summary>Set for Strava runs; null for manual runs.</summary>
    public RunType? Type { get; set; }

    /// <summary>Whether the run was tagged as a race on Strava.</summary>
    public bool IsRace { get; set; }

    public double? StartLatitude { get; set; }

    public double? StartLongitude { get; set; }
}
