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
}
