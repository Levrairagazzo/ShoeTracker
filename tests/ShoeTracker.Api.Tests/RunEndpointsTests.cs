using System.Net;
using System.Net.Http.Json;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Tests;

public class RunEndpointsTests : ApiTestBase
{
    [Fact]
    public async Task Create_ReturnsCreatedRun()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);

        var response = await client.PostAsJsonAsync($"/shoes/{shoe.Id}/runs", new CreateRunRequest(new DateOnly(2026, 1, 2), 10.5));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal($"/shoes/{shoe.Id}/runs/{run!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(new RunResponse(run.Id, new DateOnly(2026, 1, 2), 10.5, shoe.Id, RunSource.Manual), run);
    }

    [Fact]
    public async Task Create_DatedToday_IsAllowed()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);

        var run = await LogRunAsync(client, shoe.Id, 5, Today);

        Assert.Equal(Today, run.Date);
    }

    public static TheoryData<int, double, string> InvalidRuns => new()
    {
        { 0, 0, "DistanceKm" },
        { 0, -5, "DistanceKm" },
        { 1, 5, "Date" },
    };

    [Theory]
    [MemberData(nameof(InvalidRuns))]
    public async Task CreateAndUpdate_WithInvalidFields_ReturnValidationProblem(int daysFromToday, double distanceKm, string field)
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var existing = await LogRunAsync(client, shoe.Id, 5);
        var request = new CreateRunRequest(Today.AddDays(daysFromToday), distanceKm);

        var create = await client.PostAsJsonAsync($"/shoes/{shoe.Id}/runs", request);
        var update = await client.PutAsJsonAsync($"/shoes/{shoe.Id}/runs/{existing.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal([field], await ValidationErrorFieldsAsync(create));
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal([field], await ValidationErrorFieldsAsync(update));
        Assert.Equal([existing], await client.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs"));
    }

    [Fact]
    public async Task List_ReturnsOnlyThatShoesRuns_MostRecentFirst()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var otherShoe = await CreateShoeAsync(client);
        var older = await LogRunAsync(client, shoe.Id, 1, new DateOnly(2026, 1, 1));
        var newest = await LogRunAsync(client, shoe.Id, 2, new DateOnly(2026, 1, 3));
        var sameDayFirst = await LogRunAsync(client, shoe.Id, 3, new DateOnly(2026, 1, 2));
        var sameDaySecond = await LogRunAsync(client, shoe.Id, 4, new DateOnly(2026, 1, 2));
        await LogRunAsync(client, otherShoe.Id, 5);

        var runs = await client.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs");

        Assert.Equal([newest.Id, sameDaySecond.Id, sameDayFirst.Id, older.Id], runs!.Select(r => r.Id));
    }

    [Fact]
    public async Task ListAllRuns_IncludesEveryShoeAndUnassignedRuns_MostRecentFirst()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var otherShoe = await CreateShoeAsync(client);
        var oldest = await LogRunAsync(client, shoe.Id, 1, new DateOnly(2026, 1, 1));
        var unassignedId = AddUnassignedRun(OwnerEmail, 2, new DateOnly(2026, 1, 2));
        var newest = await LogRunAsync(client, otherShoe.Id, 3, new DateOnly(2026, 1, 3));

        var runs = await client.GetFromJsonAsync<List<RunResponse>>("/runs");

        Assert.Equal([newest.Id, unassignedId, oldest.Id], runs!.Select(r => r.Id));
        var unassigned = runs[1];
        Assert.Null(unassigned.ShoeId);
        Assert.Equal(RunSource.Strava, unassigned.Source);
    }

    [Fact]
    public async Task ListAllRuns_WithUnassignedFilter_ReturnsOnlyRunsWithoutAShoe()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        await LogRunAsync(client, shoe.Id, 1);
        var unassignedId = AddUnassignedRun(OwnerEmail, 2, new DateOnly(2026, 1, 2));

        var runs = await client.GetFromJsonAsync<List<RunResponse>>("/runs?unassigned=true");

        Assert.Equal([unassignedId], runs!.Select(r => r.Id));
    }

    [Fact]
    public async Task UnassignedRuns_DoNotCountTowardAnyShoe()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        await LogRunAsync(client, shoe.Id, 5);
        AddUnassignedRun(OwnerEmail, 100, new DateOnly(2026, 1, 2));

        Assert.Equal(5, (await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}"))!.TotalDistanceKm);
        Assert.Single((await client.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs"))!);
    }

    [Fact]
    public async Task Update_ChangesRunAndShoeMileage()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var run = await LogRunAsync(client, shoe.Id, 5);

        var response = await client.PutAsJsonAsync($"/shoes/{shoe.Id}/runs/{run.Id}", new CreateRunRequest(new DateOnly(2026, 1, 5), 8));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RunResponse(run.Id, new DateOnly(2026, 1, 5), 8, shoe.Id, RunSource.Manual), await response.Content.ReadFromJsonAsync<RunResponse>());
        Assert.Equal(8, (await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}"))!.TotalDistanceKm);
    }

    [Fact]
    public async Task Delete_RemovesRunAndItsMileage()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var kept = await LogRunAsync(client, shoe.Id, 5);
        var deleted = await LogRunAsync(client, shoe.Id, 7);

        var response = await client.DeleteAsync($"/shoes/{shoe.Id}/runs/{deleted.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([kept], await client.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs"));
        Assert.Equal(5, (await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}"))!.TotalDistanceKm);
    }

    [Fact]
    public async Task RunAddressedThroughTheWrongShoe_Returns404AndIsUnchanged()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var otherShoe = await CreateShoeAsync(client);
        var run = await LogRunAsync(client, shoe.Id, 5);

        var update = await client.PutAsJsonAsync($"/shoes/{otherShoe.Id}/runs/{run.Id}", new CreateRunRequest(new DateOnly(2026, 1, 5), 99));
        var delete = await client.DeleteAsync($"/shoes/{otherShoe.Id}/runs/{run.Id}");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal([run], await client.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs"));
    }

    [Fact]
    public async Task MissingShoeOrRun_Returns404()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var request = new CreateRunRequest(new DateOnly(2026, 1, 2), 5);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/shoes/999/runs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/shoes/999/runs", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/shoes/{shoe.Id}/runs/999", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/shoes/{shoe.Id}/runs/999")).StatusCode);
    }
}
