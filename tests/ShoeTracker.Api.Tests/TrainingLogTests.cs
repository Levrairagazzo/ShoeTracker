using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Tests;

/// <summary>
/// The training log's rules, tested through its own interface against in-memory SQLite: no HTTP,
/// no cookies, no WebApplicationFactory. The endpoint tests keep covering the HTTP mapping.
/// </summary>
public class TrainingLogTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ShoeTrackerContext _db;
    private readonly TrainingLog _log;
    private readonly int _ownerId;
    private readonly int _otherId;

    public TrainingLogTests()
    {
        _connection.Open();
        _db = new ShoeTrackerContext(new DbContextOptionsBuilder<ShoeTrackerContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        var owner = new User { Email = "owner@test.dev", PasswordHash = "x" };
        var other = new User { Email = "other@test.dev", PasswordHash = "x" };
        _db.Users.AddRange(owner, other);
        _db.SaveChanges();
        (_ownerId, _otherId) = (owner.Id, other.Id);
        _log = new TrainingLog(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private int AddShoe(int userId)
    {
        var shoe = new Shoe { Name = "Pegasus", Brand = "Nike", PurchaseDate = new DateOnly(2026, 1, 1), UserId = userId };
        _db.Shoes.Add(shoe);
        _db.SaveChanges();
        return shoe.Id;
    }

    private int AddRun(int userId, int? shoeId, RunSource source = RunSource.Manual)
    {
        var run = new Run
        {
            UserId = userId,
            ShoeId = shoeId,
            Date = new DateOnly(2026, 1, 2),
            DistanceKm = 10,
            Source = source,
            StravaActivityId = source == RunSource.Strava ? Random.Shared.NextInt64(1, long.MaxValue) : null,
            Type = source == RunSource.Strava ? RunType.Run : null
        };
        _db.Runs.Add(run);
        _db.SaveChanges();
        return run.Id;
    }

    // ----- Ownership: another user's data answers NotFound, never a hint that it exists -----

    [Fact]
    public async Task EveryRunMutation_OnAnotherUsersRun_IsNotFound()
    {
        var shoeId = AddShoe(_otherId);
        var runId = AddRun(_otherId, shoeId);

        Assert.IsType<TrainingLogResult.NotFound>(await _log.EditRunAsync(_ownerId, runId, new DateOnly(2026, 1, 3), 5));
        Assert.IsType<TrainingLogResult.NotFound>(await _log.DeleteRunAsync(_ownerId, runId));
        Assert.IsType<TrainingLogResult.NotFound>(await _log.MoveRunAsync(_ownerId, runId, null));
        Assert.Equal(10, _db.Runs.Single(r => r.Id == runId).DistanceKm);
    }

    [Fact]
    public async Task ShoeMutations_OnAnotherUsersShoe_AreNotFound()
    {
        var shoeId = AddShoe(_otherId);
        var ownRunId = AddRun(_ownerId, null);

        Assert.IsType<TrainingLogResult.NotFound>(await _log.LogRunAsync(_ownerId, shoeId, new DateOnly(2026, 1, 2), 5));
        Assert.IsType<TrainingLogResult.NotFound>(await _log.UpdateShoeAsync(_ownerId, shoeId, "Hijacked", "X", new DateOnly(2026, 1, 1), null));
        Assert.IsType<TrainingLogResult.NotFound>(await _log.DeleteShoeAsync(_ownerId, shoeId));
        Assert.IsType<TrainingLogResult.NotFound>(await _log.SetDefaultShoeAsync(_ownerId, shoeId));
        Assert.IsType<TrainingLogResult.NotFound>(await _log.MoveRunAsync(_ownerId, ownRunId, shoeId));
    }

    // ----- The orphaned-run bug: a run is owned through Run.UserId, not through a shoe -----

    [Fact]
    public async Task UnassignedManualRun_CanStillBeEditedAndDeleted()
    {
        var runId = AddRun(_ownerId, shoeId: null);

        var edited = await _log.EditRunAsync(_ownerId, runId, new DateOnly(2026, 1, 5), 7);
        var deleted = await _log.DeleteRunAsync(_ownerId, runId);

        var run = Assert.IsType<TrainingLogResult.Ok<Run>>(edited).Value;
        Assert.Equal((new DateOnly(2026, 1, 5), 7d), (run.Date, run.DistanceKm));
        Assert.IsType<TrainingLogResult.Done>(deleted);
        Assert.Empty(_db.Runs);
    }

    // ----- Strava runs are read-only apart from their shoe -----

    [Fact]
    public async Task StravaRun_CantBeEditedOrDeleted_ButCanBeMoved()
    {
        var shoeId = AddShoe(_ownerId);
        var runId = AddRun(_ownerId, shoeId: null, RunSource.Strava);

        Assert.IsType<TrainingLogResult.ReadOnly>(await _log.EditRunAsync(_ownerId, runId, new DateOnly(2026, 1, 3), 5));
        Assert.IsType<TrainingLogResult.ReadOnly>(await _log.DeleteRunAsync(_ownerId, runId));
        var moved = await _log.MoveRunAsync(_ownerId, runId, shoeId);

        Assert.Equal(shoeId, Assert.IsType<TrainingLogResult.Ok<Run>>(moved).Value.ShoeId);
        Assert.Equal(10, _db.Runs.Single(r => r.Id == runId).DistanceKm);
    }

    // ----- Deleting a shoe: manual runs go with it, Strava runs live on unassigned -----

    [Fact]
    public async Task DeleteShoe_DeletesManualRuns_AndUnassignsStravaRuns()
    {
        var shoeId = AddShoe(_ownerId);
        var manualId = AddRun(_ownerId, shoeId);
        var stravaId = AddRun(_ownerId, shoeId, RunSource.Strava);

        var result = await _log.DeleteShoeAsync(_ownerId, shoeId);

        Assert.IsType<TrainingLogResult.Done>(result);
        _db.ChangeTracker.Clear();
        var remaining = Assert.Single(_db.Runs.ToList());
        Assert.Equal((stravaId, (int?)null), (remaining.Id, remaining.ShoeId));
        Assert.DoesNotContain(_db.Runs, r => r.Id == manualId);
        Assert.Empty(_db.Shoes);
    }

    [Fact]
    public async Task DeleteShoe_ClearsItAsTheDefault()
    {
        var shoeId = AddShoe(_ownerId);
        await _log.SetDefaultShoeAsync(_ownerId, shoeId);

        await _log.DeleteShoeAsync(_ownerId, shoeId);

        _db.ChangeTracker.Clear();
        Assert.Null(_db.Users.Single(u => u.Id == _ownerId).DefaultShoeId);
    }

    // ----- The default shoe and where a new Strava run lands -----

    [Fact]
    public async Task ShoeForNewStravaRun_FollowsTheDefaultShoe()
    {
        var shoeId = AddShoe(_ownerId);

        Assert.Null(await _log.ShoeForNewStravaRunAsync(_ownerId));
        await _log.SetDefaultShoeAsync(_ownerId, shoeId);
        Assert.Equal(shoeId, await _log.ShoeForNewStravaRunAsync(_ownerId));
        await _log.SetDefaultShoeAsync(_ownerId, null);
        Assert.Null(await _log.ShoeForNewStravaRunAsync(_ownerId));
    }

    // ----- Validity -----

    public static TheoryData<int, double, string> InvalidRuns => new()
    {
        { 0, 0, "DistanceKm" },
        { 0, -5, "DistanceKm" },
        { 1, 5, "Date" },
    };

    [Theory]
    [MemberData(nameof(InvalidRuns))]
    public async Task LogAndEditRun_WithInvalidFields_AreInvalid(int daysFromToday, double distanceKm, string field)
    {
        var shoeId = AddShoe(_ownerId);
        var runId = AddRun(_ownerId, shoeId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysFromToday);

        var logged = await _log.LogRunAsync(_ownerId, shoeId, date, distanceKm);
        var edited = await _log.EditRunAsync(_ownerId, runId, date, distanceKm);

        Assert.Equal(field, Assert.IsType<TrainingLogResult.Invalid>(logged).Field);
        Assert.Equal(field, Assert.IsType<TrainingLogResult.Invalid>(edited).Field);
    }

    [Theory]
    [InlineData("", "Nike", null, "Name")]
    [InlineData("  ", "Nike", null, "Name")]
    [InlineData("Pegasus", "", null, "Brand")]
    [InlineData("Pegasus", "Nike", 0.0, "ThresholdKm")]
    [InlineData("Pegasus", "Nike", -100.0, "ThresholdKm")]
    public async Task CreateAndUpdateShoe_WithInvalidFields_AreInvalid(string name, string brand, double? thresholdKm, string field)
    {
        var shoeId = AddShoe(_ownerId);

        var created = await _log.CreateShoeAsync(_ownerId, name, brand, new DateOnly(2026, 1, 1), thresholdKm);
        var updated = await _log.UpdateShoeAsync(_ownerId, shoeId, name, brand, new DateOnly(2026, 1, 1), thresholdKm);

        Assert.Equal(field, Assert.IsType<TrainingLogResult.Invalid>(created).Field);
        Assert.Equal(field, Assert.IsType<TrainingLogResult.Invalid>(updated).Field);
    }

    [Fact]
    public async Task CreateShoe_WithoutAThreshold_UsesTheDefault()
    {
        var result = await _log.CreateShoeAsync(_ownerId, "Pegasus", "Nike", new DateOnly(2026, 1, 1), null);

        Assert.Equal(TrainingLog.DefaultThresholdKm, Assert.IsType<TrainingLogResult.Ok<Shoe>>(result).Value.ThresholdKm);
    }
}
