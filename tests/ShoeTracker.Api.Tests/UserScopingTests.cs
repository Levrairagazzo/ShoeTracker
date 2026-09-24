using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Tests;

public class UserScopingTests : IDisposable
{
    private const string Password = "test-password";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shoetracker-test-{Guid.NewGuid()}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public UserScopingTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ShoeTrackerContext", $"Data Source={_dbPath};Pooling=False");
            builder.UseSetting("SeedAdminEmail", "owner@test.dev");
            builder.UseSetting("SeedAdminPassword", Password);
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShoeTrackerContext>();
        db.Users.Add(new User { Email = "other@test.dev", PasswordHash = PasswordHasher.Hash(Password) });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _factory.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task ListShoes_OnlyReturnsCurrentUsersShoes()
    {
        var owner = await LoginAsync("owner@test.dev");
        var other = await LoginAsync("other@test.dev");
        var shoe = await CreateShoeAsync(owner);

        var ownerShoes = await owner.GetFromJsonAsync<List<ShoeResponse>>("/shoes");
        var otherShoes = await other.GetFromJsonAsync<List<ShoeResponse>>("/shoes");

        Assert.Equal([shoe.Id], ownerShoes!.Select(s => s.Id));
        Assert.Empty(otherShoes!);
    }

    [Fact]
    public async Task AnotherUsersShoe_IsNotFoundForEveryShoeEndpoint()
    {
        var owner = await LoginAsync("owner@test.dev");
        var other = await LoginAsync("other@test.dev");
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
        var owner = await LoginAsync("owner@test.dev");
        var other = await LoginAsync("other@test.dev");
        var shoe = await CreateShoeAsync(owner);
        var runRequest = new CreateRunRequest(new DateOnly(2026, 1, 2), 10);
        var run = await (await owner.PostAsJsonAsync($"/shoes/{shoe.Id}/runs", runRequest))
            .Content.ReadFromJsonAsync<RunResponse>();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/shoes/{shoe.Id}/runs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/shoes/{shoe.Id}/runs", runRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/shoes/{shoe.Id}/runs/{run!.Id}", runRequest with { DistanceKm = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/shoes/{shoe.Id}/runs/{run.Id}")).StatusCode);

        var runs = await owner.GetFromJsonAsync<List<RunResponse>>($"/shoes/{shoe.Id}/runs");
        Assert.Equal(10, Assert.Single(runs!).DistanceKm);
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<ShoeResponse> CreateShoeAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/shoes", new CreateShoeRequest("Pegasus", "Nike", new DateOnly(2026, 1, 1), null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShoeResponse>())!;
    }
}
