using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;
using ShoeTracker.Api.Services.Geocoding;
using ShoeTracker.Api.Services.Strava;

namespace ShoeTracker.Api.Tests;

/// <summary>
/// Runs the real API in-process against a throwaway SQLite file. Two accounts exist:
/// <see cref="OwnerEmail"/> (created by the app's startup seed step) and <see cref="OtherEmail"/>.
/// Strava is configured with dummy credentials and answered by <see cref="Strava"/>; the
/// geocoder is answered by <see cref="Nominatim"/>, and its background service is off.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    protected const string OwnerEmail = "owner@test.dev";
    protected const string OtherEmail = "other@test.dev";
    protected const string Password = "test-password";
    protected const string StravaClientId = "test-client-id";
    protected const string StravaClientSecret = "test-client-secret";
    protected const string StravaRedirectUri = "http://localhost/api/strava/callback";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shoetracker-test-{Guid.NewGuid()}.db");
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), $"shoetracker-test-keys-{Guid.NewGuid()}");

    protected FakeHttpHandler Strava { get; } = new();

    protected FakeHttpHandler Nominatim { get; } = new();

    protected WebApplicationFactory<Program> Factory { get; }

    protected ApiTestBase()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ShoeTrackerContext", $"Data Source={_dbPath};Pooling=False");
            builder.UseSetting("SeedAdminEmail", OwnerEmail);
            builder.UseSetting("SeedAdminPassword", Password);
            // Overrides any real credentials from user-secrets.
            builder.UseSetting("Strava:ClientId", StravaClientId);
            builder.UseSetting("Strava:ClientSecret", StravaClientSecret);
            builder.UseSetting("Strava:RedirectUri", StravaRedirectUri);
            builder.UseSetting("DataProtection:KeysPath", _keysPath);
            // Tests resolve place names by calling PlaceNameResolver directly, without the rate-limit pause.
            builder.UseSetting("Geocoding:Enabled", "false");
            builder.UseSetting("Geocoding:MinInterval", "00:00:00");
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<StravaClient>().ConfigurePrimaryHttpMessageHandler(() => Strava);
                services.AddHttpClient<NominatimClient>().ConfigurePrimaryHttpMessageHandler(() => Nominatim);
            });
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
        if (Directory.Exists(_keysPath)) Directory.Delete(_keysPath, recursive: true);
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
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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

    /// <summary>
    /// Inserts a run with no shoe directly into the DB, standing in for an imported Strava run
    /// until the import exists.
    /// </summary>
    protected int AddUnassignedRun(string email, double distanceKm, DateOnly date)
    {
        var id = 0;
        WithDb(db =>
        {
            var run = new Run
            {
                UserId = db.Users.Single(u => u.Email == email).Id,
                Date = date,
                DistanceKm = distanceKm,
                Source = RunSource.Strava
            };
            db.Runs.Add(run);
            db.SaveChanges();
            id = run.Id;
        });
        return id;
    }

    /// <summary>
    /// Stores a Strava connection for the user directly (skipping the OAuth flow), with the
    /// given access token valid for the next 6 hours.
    /// </summary>
    protected async Task ConnectStravaAsync(string email, string accessToken = "access-token")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShoeTrackerContext>();
        var userId = db.Users.Single(u => u.Email == email).Id;
        await scope.ServiceProvider.GetRequiredService<StravaTokenStore>().SaveAsync(
            userId,
            new StravaTokenResponse(accessToken, "refresh-token", DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds(),
                new StravaAthlete(12345, "Test", "Runner")),
            "read,activity:read_all");
    }

    /// <summary>Returns the field names in a ValidationProblem response's <c>errors</c> object.</summary>
    protected static async Task<IReadOnlyList<string>> ValidationErrorFieldsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }
}
