using System.Net;
using System.Net.Http.Json;
using ShoeTracker.Api.Dtos;

namespace ShoeTracker.Api.Tests;

public class UserScopingTests : ApiTestBase
{
    [Fact]
    public async Task ListShoes_OnlyReturnsCurrentUsersShoes()
    {
        var owner = await LoginAsync(OwnerEmail);
        var other = await LoginAsync(OtherEmail);
        var shoe = await CreateShoeAsync(owner);

        var ownerShoes = await owner.GetFromJsonAsync<List<ShoeResponse>>("/shoes");
        var otherShoes = await other.GetFromJsonAsync<List<ShoeResponse>>("/shoes");

        Assert.Equal([shoe.Id], ownerShoes!.Select(s => s.Id));
        Assert.Empty(otherShoes!);
    }

    [Fact]
    public async Task AnotherUsersShoe_IsNotFoundForEveryShoeEndpoint()
    {
        var owner = await LoginAsync(OwnerEmail);
        var other = await LoginAsync(OtherEmail);
        var shoe = await CreateShoeAsync(owner);
        var update = new CreateShoeRequest("Hijacked", "X", new DateOnly(2026, 1, 1), null);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/shoes/{shoe.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/shoes/{shoe.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/shoes/{shoe.Id}")).StatusCode);

        var unchanged = await owner.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}");
        Assert.Equal(shoe.Name, unchanged!.Name);
    }

    [Fact]
    public async Task AnotherUsersRuns_AreNotFoundForEveryRunEndpoint()
    {
        var owner = await LoginAsync(OwnerEmail);
        var other = await LoginAsync(OtherEmail);
        var shoe = await CreateShoeAsync(owner);
        var runRequest = new CreateRunRequest(new DateOnly(2026, 1, 2), 10);
        var run = await LogRunAsync(owner, shoe.Id, 10);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/shoes/{shoe.Id}/runs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/shoes/{shoe.Id}/runs", runRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/shoes/{shoe.Id}/runs/{run.Id}", runRequest with { DistanceKm = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/shoes/{shoe.Id}/runs/{run.Id}")).StatusCode);

        var runs = await owner.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs");
        Assert.Equal(10, Assert.Single(runs!).DistanceKm);
    }

    [Fact]
    public async Task ListAllRuns_OnlyReturnsCurrentUsersRuns_IncludingUnassigned()
    {
        var owner = await LoginAsync(OwnerEmail);
        var other = await LoginAsync(OtherEmail);
        var shoe = await CreateShoeAsync(owner);
        var assigned = await LogRunAsync(owner, shoe.Id, 10);
        var unassignedId = AddUnassignedRun(OwnerEmail, 20, new DateOnly(2026, 1, 1));
        var otherUnassignedId = AddUnassignedRun(OtherEmail, 30, new DateOnly(2026, 1, 1));

        var ownerRuns = await owner.GetFromJsonAsync<List<RunResponse>>("/runs");
        var otherRuns = await other.GetFromJsonAsync<List<RunResponse>>("/runs");
        var otherUnassigned = await other.GetFromJsonAsync<List<RunResponse>>("/runs?unassigned=true");

        Assert.Equal([assigned.Id, unassignedId], ownerRuns!.Select(r => r.Id));
        Assert.Equal([otherUnassignedId], otherRuns!.Select(r => r.Id));
        Assert.Equal([otherUnassignedId], otherUnassigned!.Select(r => r.Id));
    }
}
