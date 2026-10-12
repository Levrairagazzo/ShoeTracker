using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Tests;

/// <summary>Checks data migrations against a database built at the migration before them.</summary>
public class MigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shoetracker-migration-test-{Guid.NewGuid()}.db");
    private readonly ShoeTrackerContext _db;

    public MigrationTests()
    {
        _db = new ShoeTrackerContext(new DbContextOptionsBuilder<ShoeTrackerContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddRunOwnershipAndSource_GivesExistingRunsTheirShoeOwnerAndManualSource()
    {
        _db.GetService<IMigrator>().Migrate("AddShoeOwnership");
        _db.Database.ExecuteSqlRaw("""
            INSERT INTO Users (Id, Email, PasswordHash) VALUES (1, 'a@test.dev', 'x'), (2, 'b@test.dev', 'x');
            INSERT INTO Shoes (Id, Name, Brand, PurchaseDate, ThresholdKm, IsRetired, UserId)
                VALUES (1, 'A', 'Nike', '2026-01-01', 700, 0, 1), (2, 'B', 'Asics', '2026-01-01', 700, 0, 2);
            INSERT INTO Runs (Id, Date, DistanceKm, ShoeId) VALUES (1, '2026-01-02', 5, 1), (2, '2026-01-03', 6, 2), (3, '2026-01-04', 7, 1);
            """);

        _db.Database.Migrate();

        var runs = _db.Runs.OrderBy(r => r.Id).ToList();
        Assert.Equal([1, 2, 1], runs.Select(r => r.UserId));
        Assert.Equal([1, 2, 1], runs.Select(r => r.ShoeId));
        Assert.All(runs, r => Assert.Equal(RunSource.Manual, r.Source));
        Assert.All(runs, r => Assert.Null(r.StravaActivityId));
    }

    [Fact]
    public void AddStravaHistoryImportedAt_MarksConnectionsWithImportedRunsAsHavingTheirHistory()
    {
        _db.GetService<IMigrator>().Migrate("AddDefaultShoe");
        _db.Database.ExecuteSqlRaw("""
            INSERT INTO Users (Id, Email, PasswordHash) VALUES (1, 'a@test.dev', 'x'), (2, 'b@test.dev', 'x');
            INSERT INTO StravaConnections (Id, UserId, AthleteId, AthleteName, EncryptedAccessToken, EncryptedRefreshToken, ExpiresAt, Scope, ConnectedAt)
                VALUES (1, 1, 11, 'A', 'x', 'x', '2026-09-01 00:00:00+00:00', 'activity:read_all', '2026-09-01 10:00:00+00:00'),
                       (2, 2, 22, 'B', 'x', 'x', '2026-09-01 00:00:00+00:00', 'activity:read_all', '2026-09-02 10:00:00+00:00');
            INSERT INTO Runs (Id, UserId, Date, DistanceKm, Source, StravaActivityId, IsRace) VALUES (1, 1, '2026-08-01', 5, 'Strava', 100, 0);
            """);

        _db.Database.Migrate();

        var connections = _db.StravaConnections.OrderBy(c => c.Id).ToList();
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), connections[0].HistoryImportedAt);
        Assert.Null(connections[1].HistoryImportedAt);
    }
}
