using System.Net;
using System.Net.Http.Json;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services.Strava;

namespace ShoeTracker.Api.Tests;

public class StravaImportTests : ApiTestBase
{
    /// <summary>Serves <paramref name="activities"/> from the activities API, paged like Strava.</summary>
    private void ServeActivities(IReadOnlyList<object> activities)
    {
        Strava.Respond = request =>
        {
            if (request.Path != "/api/v3/athlete/activities") return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            var page = int.Parse(request.Query["page"]);
            var perPage = int.Parse(request.Query["per_page"]);
            return FakeHttpHandler.Json(activities.Skip((page - 1) * perPage).Take(perPage).ToList());
        };
    }

    private static async Task<StravaImportResponse> ImportAsync(HttpClient client)
    {
        var response = await client.PostAsync("/strava/import", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StravaImportResponse>())!;
    }

    private List<Run> StravaRuns()
    {
        var runs = new List<Run>();
        WithDb(db => runs = db.Runs.Where(r => r.Source == RunSource.Strava).OrderBy(r => r.StravaActivityId).ToList());
        return runs;
    }

    [Fact]
    public async Task Import_StoresRunsAsUnassignedStravaRunsInKm()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail, accessToken: "the-token");
        ServeActivities([
            FakeHttpHandler.Activity(101, 10_234.6, "2026-03-01T07:15:00Z"),
            // Local date, not UTC: a late-evening run stays on the day it was run.
            FakeHttpHandler.Activity(102, 5_000, "2026-03-02T23:30:00Z", sportType: "TrailRun"),
            FakeHttpHandler.Activity(103, 8_000, "2026-03-03T06:00:00Z", sportType: "VirtualRun"),
        ]);

        var result = await ImportAsync(client);

        Assert.Equal(3, result.Imported);
        var runs = StravaRuns();
        Assert.Equal([101L, 102L, 103L], runs.Select(r => r.StravaActivityId!.Value));
        Assert.Equal([10.235, 5, 8], runs.Select(r => r.DistanceKm));
        Assert.Equal([new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)], runs.Select(r => r.Date));
        Assert.All(runs, r => Assert.Null(r.ShoeId));
        var request = Assert.Single(Strava.Requests);
        Assert.Equal("Bearer the-token", request.Authorization);
        Assert.False(request.Query.ContainsKey("after"));
    }

    [Fact]
    public async Task Import_SkipsNonRunsAndZeroDistanceActivities()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([
            FakeHttpHandler.Activity(1, 30_000, "2026-03-01T07:00:00Z", sportType: "Ride"),
            FakeHttpHandler.Activity(2, 2_000, "2026-03-01T08:00:00Z", sportType: "Walk"),
            FakeHttpHandler.Activity(3, 0, "2026-03-01T09:00:00Z"),
            FakeHttpHandler.Activity(4, 6_000, "2026-03-01T10:00:00Z"),
        ]);

        var result = await ImportAsync(client);

        Assert.Equal(1, result.Imported);
        Assert.Equal([4L], StravaRuns().Select(r => r.StravaActivityId!.Value));
    }

    [Fact]
    public async Task Import_RecordsTypeRaceAndStartPoint()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([
            FakeHttpHandler.Activity(1, 10_000, "2026-03-01T07:00:00Z", startLatLng: [37.8044, -122.2712]),
            FakeHttpHandler.Activity(2, 10_000, "2026-03-02T07:00:00Z", sportType: "TrailRun"),
            FakeHttpHandler.Activity(3, 10_000, "2026-03-03T07:00:00Z", sportType: "VirtualRun"),
            // A treadmill run recorded as a plain "Run", flagged by Strava as indoor.
            FakeHttpHandler.Activity(4, 10_000, "2026-03-04T07:00:00Z", trainer: true),
            FakeHttpHandler.Activity(5, 60_149, "2026-03-05T07:00:00Z", workoutType: StravaImporter.RaceWorkoutType),
        ]);

        await ImportAsync(client);

        var runs = await client.GetFromJsonAsync<List<RunResponse>>("/runs?source=Strava");
        var byId = runs!.ToDictionary(r => r.Date.Day);
        Assert.Equal(RunType.Run, byId[1].Type);
        Assert.Equal(RunType.Trail, byId[2].Type);
        Assert.Equal(RunType.Treadmill, byId[3].Type);
        Assert.Equal(RunType.Treadmill, byId[4].Type);
        Assert.True(byId[5].IsRace);
        Assert.True(byId[5].IsUltra);
        Assert.Equal([false, false, false, false], new[] { 1, 2, 3, 4 }.Select(d => byId[d].IsRace || byId[d].IsUltra));
        var stored = StravaRuns();
        Assert.Equal((37.8044, -122.2712), (stored[0].StartLatitude!.Value, stored[0].StartLongitude!.Value));
        Assert.All(stored.Skip(1), r => Assert.Null(r.StartLatitude));
    }

    [Fact]
    public async Task Reimport_UpdatesAlreadyImportedRunsWithStravasCurrentValues()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([FakeHttpHandler.Activity(1, 5_000, "2026-03-01T07:00:00Z")]);
        await ImportAsync(client);
        ServeActivities([FakeHttpHandler.Activity(1, 5_500, "2026-03-01T07:00:00Z", sportType: "TrailRun", workoutType: 1)]);

        var result = await ImportAsync(client);

        Assert.Equal(0, result.Imported);
        var run = Assert.Single(StravaRuns());
        Assert.Equal(5.5, run.DistanceKm);
        Assert.Equal(RunType.Trail, run.Type);
        Assert.True(run.IsRace);
    }

    [Fact]
    public async Task Reimport_WhenRunsPredateRunTypes_FetchesTheWholeHistoryToBackfillThem()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([
            FakeHttpHandler.Activity(1, 5_000, "2026-01-01T07:00:00Z", sportType: "TrailRun"),
            FakeHttpHandler.Activity(2, 5_000, "2026-03-01T07:00:00Z"),
        ]);
        await ImportAsync(client);
        // As imported before Type existed.
        WithDb(db =>
        {
            foreach (var run in db.Runs) run.Type = null;
            db.SaveChanges();
        });
        Strava.Requests.Clear();

        await ImportAsync(client);

        Assert.False(Assert.Single(Strava.Requests).Query.ContainsKey("after"));
        Assert.Equal([RunType.Trail, RunType.Run], StravaRuns().Select(r => r.Type));
    }

    [Fact]
    public async Task FirstImport_LeavesHistoryUnassigned_EvenWithADefaultShoe()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        await SetDefaultShoeAsync(client, shoe.Id);
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([FakeHttpHandler.Activity(1, 5_000, "2026-03-01T07:00:00Z")]);

        await ImportAsync(client);

        Assert.Null(Assert.Single(StravaRuns()).ShoeId);
    }

    [Fact]
    public async Task LaterImports_AssignNewRunsToTheDefaultShoe_AndKeepExistingAssignments()
    {
        var client = await LoginAsync();
        var defaultShoe = await CreateShoeAsync(client);
        var otherShoe = await CreateShoeAsync(client);
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([
            FakeHttpHandler.Activity(1, 5_000, "2026-03-01T07:00:00Z"),
            FakeHttpHandler.Activity(2, 6_000, "2026-03-02T07:00:00Z"),
        ]);
        await ImportAsync(client);
        var history = StravaRuns();
        await client.PutAsJsonAsync($"/runs/{history[1].Id}/shoe", new AssignRunRequest(otherShoe.Id));
        await SetDefaultShoeAsync(client, defaultShoe.Id);
        ServeActivities([
            FakeHttpHandler.Activity(2, 6_000, "2026-03-02T07:00:00Z"),
            FakeHttpHandler.Activity(3, 7_000, "2026-03-03T07:00:00Z"),
        ]);

        await ImportAsync(client);

        var runs = StravaRuns();
        Assert.Equal([null, otherShoe.Id, defaultShoe.Id], runs.Select(r => r.ShoeId));
    }

    [Fact]
    public async Task LaterImports_WithoutADefaultShoe_LeaveNewRunsUnassigned()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([FakeHttpHandler.Activity(1, 5_000, "2026-03-01T07:00:00Z")]);
        await ImportAsync(client);
        ServeActivities([FakeHttpHandler.Activity(2, 5_000, "2026-03-02T07:00:00Z")]);

        await ImportAsync(client);

        Assert.All(StravaRuns(), r => Assert.Null(r.ShoeId));
    }

    [Fact]
    public async Task Import_PagesThroughTheWholeHistory()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        var total = StravaImporter.PageSize * 2 + 7;
        ServeActivities(Enumerable.Range(1, total)
            .Select(i => FakeHttpHandler.Activity(i, 5_000, "2026-01-01T07:00:00Z"))
            .ToList());

        var result = await ImportAsync(client);

        Assert.Equal(total, result.Imported);
        Assert.Equal(["1", "2", "3"], Strava.Requests.Select(r => r.Query["page"]));
        Assert.All(Strava.Requests, r => Assert.Equal(StravaImporter.PageSize.ToString(), r.Query["per_page"]));
    }

    [Fact]
    public async Task Reimport_OnlyAsksForRecentActivities_AndSkipsOnesAlreadyImported()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([
            FakeHttpHandler.Activity(1, 5_000, "2026-03-01T07:00:00Z"),
            FakeHttpHandler.Activity(2, 5_000, "2026-03-10T07:00:00Z"),
        ]);
        await ImportAsync(client);
        Strava.Requests.Clear();
        // Strava honours "after" itself; the fake ignores it, so the overlap is resent here.
        ServeActivities([
            FakeHttpHandler.Activity(2, 5_000, "2026-03-10T07:00:00Z"),
            FakeHttpHandler.Activity(3, 7_000, "2026-03-11T07:00:00Z"),
        ]);

        var result = await ImportAsync(client);

        Assert.Equal(1, result.Imported);
        Assert.Equal([1L, 2L, 3L], StravaRuns().Select(r => r.StravaActivityId!.Value));
        var after = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Assert.Single(Strava.Requests).Query["after"]));
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero), after);
    }

    [Fact]
    public async Task ImportedRuns_ShowUpAsUnassignedOnlyForTheirOwner_AndInStatus()
    {
        var owner = await LoginAsync(OwnerEmail);
        var other = await LoginAsync(OtherEmail);
        await ConnectStravaAsync(OwnerEmail);
        ServeActivities([FakeHttpHandler.Activity(1, 5_000, "2026-03-01T07:00:00Z")]);

        await ImportAsync(owner);

        var ownerUnassigned = await owner.GetFromJsonAsync<List<RunResponse>>("/runs?unassigned=true");
        Assert.Equal(RunSource.Strava, Assert.Single(ownerUnassigned!).Source);
        Assert.Empty((await other.GetFromJsonAsync<List<RunResponse>>("/runs"))!);
        Assert.Equal(1, (await owner.GetFromJsonAsync<StravaStatusResponse>("/strava/status"))!.ImportedRuns);
        Assert.Equal(0, (await other.GetFromJsonAsync<StravaStatusResponse>("/strava/status"))!.ImportedRuns);
    }

    [Fact]
    public async Task Import_WhenNotConnected_Returns409()
    {
        var client = await LoginAsync();

        var response = await client.PostAsync("/strava/import", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(Strava.Requests);
    }

    [Fact]
    public async Task Import_WhenAConcurrentImportSavedTheSameActivityFirst_Returns409()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        Strava.Respond = _ =>
        {
            // Simulates another import for this user finishing while this one is fetching.
            WithDb(db =>
            {
                db.Runs.Add(new Run
                {
                    UserId = db.Users.Single(u => u.Email == OwnerEmail).Id,
                    Source = RunSource.Strava,
                    StravaActivityId = 1,
                    Date = new DateOnly(2026, 3, 1),
                    DistanceKm = 5
                });
                db.SaveChanges();
            });
            return FakeHttpHandler.Json(new[] { FakeHttpHandler.Activity(1, 5_000, "2026-03-01T07:00:00Z") });
        };

        var response = await client.PostAsync("/strava/import", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(StravaRuns());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway)]
    public async Task Import_WhenStravaFails_ReturnsProblemAndImportsNothing(HttpStatusCode stravaStatus, HttpStatusCode expected)
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        var calls = 0;
        // First page succeeds, second fails: nothing from the partial import may be kept.
        Strava.Respond = _ => ++calls == 1
            ? FakeHttpHandler.Json(Enumerable.Range(1, StravaImporter.PageSize)
                .Select(i => FakeHttpHandler.Activity(i, 5_000, "2026-01-01T07:00:00Z")).ToList())
            : new HttpResponseMessage(stravaStatus);

        var response = await client.PostAsync("/strava/import", null);

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(StravaRuns());
    }
}
