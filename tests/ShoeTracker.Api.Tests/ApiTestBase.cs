using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Tests;

/// <summary>
/// Runs the real API in-process against a throwaway SQLite file. Two accounts exist:
/// <see cref="OwnerEmail"/> (created by the app's startup seed step) and <see cref="OtherEmail"/>.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    protected const string OwnerEmail = "owner@test.dev";
    protected const string OtherEmail = "other@test.dev";
    protected const string Password = "test-password";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shoetracker-test-{Guid.NewGuid()}.db");

    protected WebApplicationFactory<Program> Factory { get; }

    protected ApiTestBase()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ShoeTrackerContext", $"Data Source={_dbPath};Pooling=False");
            builder.UseSetting("SeedAdminEmail", OwnerEmail);
            builder.UseSetting("SeedAdminPassword", Password);
        });

        WithDb(db =>
        {
            db.Users.Add(new User { Email = OtherEmail, PasswordHash = PasswordHasher.Hash(Password) });
            db.SaveChanges();
        });
    }

    public void Dispose()
    {
        Factory.Dispose();
        File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    protected void WithDb(Action<ShoeTrackerContext> action)
    {
        using var scope = Factory.Services.CreateScope();
        action(scope.ServiceProvider.GetRequiredService<ShoeTrackerContext>());
    }

    protected async Task<HttpClient> LoginAsync(string email = OwnerEmail)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    protected static async Task<ShoeResponse> CreateShoeAsync(HttpClient client, double? thresholdKm = null)
    {
        var response = await client.PostAsJsonAsync("/shoes", new CreateShoeRequest("Pegasus", "Nike", new DateOnly(2026, 1, 1), thresholdKm));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShoeResponse>())!;
    }

    protected static async Task<RunResponse> LogRunAsync(HttpClient client, int shoeId, double distanceKm, DateOnly? date = null)
    {
        var response = await client.PostAsJsonAsync($"/shoes/{shoeId}/runs", new CreateRunRequest(date ?? new DateOnly(2026, 1, 2), distanceKm));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RunResponse>())!;
    }

    /// <summary>Returns the field names in a ValidationProblem response's <c>errors</c> object.</summary>
    protected static async Task<IReadOnlyList<string>> ValidationErrorFieldsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }
}
