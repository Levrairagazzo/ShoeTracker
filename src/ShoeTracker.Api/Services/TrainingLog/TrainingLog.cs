using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Services;

/// <summary>
/// The user's training log: the single owner of the rules for changing runs and shoes. Every
/// mutation goes through here, so each rule has one home: a user only ever touches their own
/// data (<see cref="TrainingLogResult.NotFound"/>), Strava runs are read-only apart from their
/// shoe (<see cref="TrainingLogResult.ReadOnly"/>), deleting a shoe deletes its manual runs but
/// keeps its Strava runs as unassigned, newly arriving Strava runs land on the default shoe,
/// and validity is decided here (<see cref="TrainingLogResult.Invalid"/>). Each method is one
/// atomic change. Reads stay plain EF queries in the endpoints.
/// </summary>
public class TrainingLog(ShoeTrackerContext db)
{
    public const double DefaultThresholdKm = 700;

    // Strava is the source of truth for its runs: a local edit would be overwritten by the next
    // import, and a local delete undone by it. Only the shoe can change (MoveRunAsync).
    private static readonly TrainingLogResult StravaRunIsReadOnly = new TrainingLogResult.ReadOnly(
        "Runs imported from Strava can't be edited or deleted here. Change them on Strava, or move them to another shoe.");

    public async Task<TrainingLogResult> CreateShoeAsync(int userId, string name, string brand, DateOnly purchaseDate, double? thresholdKm)
    {
        var threshold = thresholdKm ?? DefaultThresholdKm;
        if (ValidateShoe(name, brand, threshold) is { } invalid) return invalid;

        var shoe = new Shoe
        {
            Name = name,
            Brand = brand,
            PurchaseDate = purchaseDate,
            ThresholdKm = threshold,
            UserId = userId
        };

        db.Shoes.Add(shoe);
        await db.SaveChangesAsync();
        return new TrainingLogResult.Ok<Shoe>(shoe);
    }

    public async Task<TrainingLogResult> UpdateShoeAsync(int userId, int shoeId, string name, string brand, DateOnly purchaseDate, double? thresholdKm)
    {
        var threshold = thresholdKm ?? DefaultThresholdKm;
        if (ValidateShoe(name, brand, threshold) is { } invalid) return invalid;

        // Runs come along so the caller can report the updated shoe's mileage.
        var shoe = await db.Shoes.Include(s => s.Runs).FirstOrDefaultAsync(s => s.Id == shoeId && s.UserId == userId);
        if (shoe is null) return TrainingLogResult.NotFound.Instance;

        shoe.Name = name;
        shoe.Brand = brand;
        shoe.PurchaseDate = purchaseDate;
        shoe.ThresholdKm = threshold;

        await db.SaveChangesAsync();
        return new TrainingLogResult.Ok<Shoe>(shoe);
    }

    public async Task<TrainingLogResult> DeleteShoeAsync(int userId, int shoeId)
    {
        if (!await OwnsShoeAsync(userId, shoeId)) return TrainingLogResult.NotFound.Instance;

        // A shoe takes its manual runs with it, but Strava owns its runs: those live on,
        // unassigned. The user's DefaultShoeId clears through the FK's set-null.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Runs
            .Where(r => r.ShoeId == shoeId && r.Source == RunSource.Strava)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.ShoeId, (int?)null));
        await db.Runs
            .Where(r => r.ShoeId == shoeId && r.Source == RunSource.Manual)
            .ExecuteDeleteAsync();
        await db.Shoes.Where(s => s.Id == shoeId).ExecuteDeleteAsync();
        await transaction.CommitAsync();

        return TrainingLogResult.Done.Instance;
    }

    public async Task<TrainingLogResult> SetDefaultShoeAsync(int userId, int? shoeId)
    {
        if (shoeId is { } id && !await OwnsShoeAsync(userId, id)) return TrainingLogResult.NotFound.Instance;

        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.DefaultShoeId, shoeId));

        return TrainingLogResult.Done.Instance;
    }

    /// <summary>
    /// The shoe a newly arriving Strava run is assigned to: the default shoe, or null to stay
    /// unassigned. (The first import after connecting is the user's history and ignores this.)
    /// </summary>
    public Task<int?> ShoeForNewStravaRunAsync(int userId, CancellationToken ct = default) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.DefaultShoeId).SingleAsync(ct);

    public async Task<TrainingLogResult> LogRunAsync(int userId, int shoeId, DateOnly date, double distanceKm)
    {
        if (ValidateRun(date, distanceKm) is { } invalid) return invalid;
        if (!await OwnsShoeAsync(userId, shoeId)) return TrainingLogResult.NotFound.Instance;

        var run = new Run
        {
            UserId = userId,
            ShoeId = shoeId,
            Source = RunSource.Manual,
            Date = date,
            DistanceKm = distanceKm
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return new TrainingLogResult.Ok<Run>(run);
    }

    public async Task<TrainingLogResult> EditRunAsync(int userId, int runId, DateOnly date, double distanceKm)
    {
        if (ValidateRun(date, distanceKm) is { } invalid) return invalid;

        var run = await FindRunAsync(userId, runId);
        if (run is null) return TrainingLogResult.NotFound.Instance;
        if (run.Source == RunSource.Strava) return StravaRunIsReadOnly;

        run.Date = date;
        run.DistanceKm = distanceKm;

        await db.SaveChangesAsync();
        return new TrainingLogResult.Ok<Run>(run);
    }

    public async Task<TrainingLogResult> DeleteRunAsync(int userId, int runId)
    {
        var run = await FindRunAsync(userId, runId);
        if (run is null) return TrainingLogResult.NotFound.Instance;
        if (run.Source == RunSource.Strava) return StravaRunIsReadOnly;

        db.Runs.Remove(run);
        await db.SaveChangesAsync();
        return TrainingLogResult.Done.Instance;
    }

    public async Task<TrainingLogResult> MoveRunAsync(int userId, int runId, int? shoeId)
    {
        var run = await FindRunAsync(userId, runId);
        if (run is null) return TrainingLogResult.NotFound.Instance;
        if (shoeId is { } id && !await OwnsShoeAsync(userId, id)) return TrainingLogResult.NotFound.Instance;

        run.ShoeId = shoeId;
        await db.SaveChangesAsync();
        return new TrainingLogResult.Ok<Run>(run);
    }

    // A run is owned through Run.UserId, never through its shoe: an unassigned run is still
    // the user's to change.
    private Task<Run?> FindRunAsync(int userId, int runId) =>
        db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.UserId == userId);

    private Task<bool> OwnsShoeAsync(int userId, int shoeId) =>
        db.Shoes.AnyAsync(s => s.Id == shoeId && s.UserId == userId);

    private static TrainingLogResult.Invalid? ValidateShoe(string name, string brand, double thresholdKm)
    {
        if (string.IsNullOrWhiteSpace(name)) return new("Name", "Name is required.");
        if (string.IsNullOrWhiteSpace(brand)) return new("Brand", "Brand is required.");
        if (thresholdKm <= 0) return new("ThresholdKm", "ThresholdKm must be greater than zero.");
        return null;
    }

    private static TrainingLogResult.Invalid? ValidateRun(DateOnly date, double distanceKm)
    {
        if (distanceKm <= 0) return new("DistanceKm", "DistanceKm must be greater than zero.");
        if (date > DateOnly.FromDateTime(DateTime.UtcNow)) return new("Date", "Date cannot be in the future.");
        return null;
    }
}
