using System.Net;
using System.Net.Http.Json;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Tests;

/// <summary>Default shoe, moving runs between shoes, and what Strava runs allow.</summary>
public class RunAssignmentTests : ApiTestBase
{
    private static async Task<List<ShoeResponse>> ShoesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ShoeResponse>>("/shoes"))!;

    [Fact]
    public async Task SetDefaultShoe_MarksOnlyThatShoe_AndCanBeMovedOrCleared()
    {
        var client = await LoginAsync();
        var first = await CreateShoeAsync(client);
        var second = await CreateShoeAsync(client);

        await SetDefaultShoeAsync(client, first.Id);
        var afterFirst = await ShoesAsync(client);
        await SetDefaultShoeAsync(client, second.Id);
        var afterSecond = await ShoesAsync(client);
        await SetDefaultShoeAsync(client, null);
        var afterClear = await ShoesAsync(client);

        Assert.Equal([first.Id], afterFirst.Where(s => s.IsDefault).Select(s => s.Id));
        Assert.Equal([second.Id], afterSecond.Where(s => s.IsDefault).Select(s => s.Id));
        Assert.True((await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{second.Id}"))!.IsDefault is false);
        Assert.DoesNotContain(afterClear, s => s.IsDefault);
    }

    [Fact]
    public async Task SetDefaultShoe_ToMissingShoe_Returns404()
    {
        var client = await LoginAsync();

        var response = await client.PutAsJsonAsync("/shoes/default", new SetDefaultShoeRequest(999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletingTheDefaultShoe_ClearsTheDefault()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        await SetDefaultShoeAsync(client, shoe.Id);

        await client.DeleteAsync($"/shoes/{shoe.Id}");

        WithDb(db => Assert.Null(db.Users.Single(u => u.Email == OwnerEmail).DefaultShoeId));
    }

    [Fact]
    public async Task DeletingAShoe_UnassignsItsStravaRuns_AndDeletesItsManualRuns()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var manual = await LogRunAsync(client, shoe.Id, 5);
        var strava = AddStravaRun(OwnerEmail, 10, shoe.Id);

        await client.DeleteAsync($"/shoes/{shoe.Id}");

        var runs = await client.GetFromJsonAsync<List<RunResponse>>("/runs");
        var remaining = Assert.Single(runs!);
        Assert.Equal(strava, remaining.Id);
        Assert.Null(remaining.ShoeId);
        WithDb(db => Assert.DoesNotContain(db.Runs, r => r.Id == manual.Id));
    }

    [Fact]
    public async Task MoveRun_BetweenShoesAndToUnassigned_UpdatesBothShoesMileage()
    {
        var client = await LoginAsync();
        var from = await CreateShoeAsync(client);
        var to = await CreateShoeAsync(client);
        var run = await LogRunAsync(client, from.Id, 8);

        var moved = await client.PutAsJsonAsync($"/runs/{run.Id}/shoe", new AssignRunRequest(to.Id));
        var afterMove = await ShoesAsync(client);
        var unassigned = await client.PutAsJsonAsync($"/runs/{run.Id}/shoe", new AssignRunRequest(null));
        var afterUnassign = await ShoesAsync(client);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(to.Id, (await moved.Content.ReadFromJsonAsync<RunResponse>())!.ShoeId);
        Assert.Equal((0, 8), (afterMove.Single(s => s.Id == from.Id).TotalDistanceKm, afterMove.Single(s => s.Id == to.Id).TotalDistanceKm));
        Assert.Null((await unassigned.Content.ReadFromJsonAsync<RunResponse>())!.ShoeId);
        Assert.All(afterUnassign, s => Assert.Equal(0, s.TotalDistanceKm));
    }

    [Fact]
    public async Task MoveRun_AssignsAnUnassignedStravaRun()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var runId = AddStravaRun(OwnerEmail, 12);

        await client.PutAsJsonAsync($"/runs/{runId}/shoe", new AssignRunRequest(shoe.Id));

        Assert.Equal(12, (await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}"))!.TotalDistanceKm);
        Assert.Empty((await client.GetFromJsonAsync<List<RunResponse>>("/runs?unassigned=true"))!);
    }

    [Fact]
    public async Task MoveRun_ToMissingShoeOrMissingRun_Returns404()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var run = await LogRunAsync(client, shoe.Id, 5);

        var missingShoe = await client.PutAsJsonAsync($"/runs/{run.Id}/shoe", new AssignRunRequest(999));
        var missingRun = await client.PutAsJsonAsync("/runs/999/shoe", new AssignRunRequest(shoe.Id));

        Assert.Equal(HttpStatusCode.NotFound, missingShoe.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingRun.StatusCode);
        Assert.Equal(shoe.Id, Assert.Single((await client.GetFromJsonAsync<List<RunResponse>>("/runs"))!).ShoeId);
    }

    [Fact]
    public async Task StravaRuns_CantBeEditedOrDeletedThroughTheShoeRoutes()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var runId = AddStravaRun(OwnerEmail, 10, shoe.Id);

        var update = await client.PutAsJsonAsync($"/shoes/{shoe.Id}/runs/{runId}", new CreateRunRequest(new DateOnly(2026, 1, 5), 99));
        var delete = await client.DeleteAsync($"/shoes/{shoe.Id}/runs/{runId}");

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var run = Assert.Single((await client.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs"))!);
        Assert.Equal(10, run.DistanceKm);
    }

    [Fact]
    public async Task StravaStatus_CountsUnassignedRuns()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        AddStravaRun(OwnerEmail, 5);
        AddStravaRun(OwnerEmail, 6);
        AddStravaRun(OwnerEmail, 7, shoe.Id);

        var status = await client.GetFromJsonAsync<StravaStatusResponse>("/strava/status");

        Assert.Equal((3, 2), (status!.ImportedRuns, status.UnassignedRuns));
    }
}
