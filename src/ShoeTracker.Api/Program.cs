using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Endpoints;
using ShoeTracker.Api.Services;
using ShoeTracker.Api.Services.Geocoding;
using ShoeTracker.Api.Services.Strava;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShoeTrackerContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ShoeTrackerContext")
        ?? throw new InvalidOperationException("Connection string 'ShoeTrackerContext' not found.")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ShoeTracker.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// Protects the auth cookie and the stored Strava tokens. In Docker the key ring lives on the
// data volume (DataProtection__KeysPath), so sessions and tokens survive container rebuilds.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("ShoeTracker");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.Configure<StravaOptions>(builder.Configuration.GetSection(StravaOptions.SectionName));
builder.Services.AddHttpClient<StravaClient>(client => client.BaseAddress = StravaClient.BaseAddress);
builder.Services.AddScoped<StravaTokenStore>();
builder.Services.AddScoped<StravaImporter>();

builder.Services.Configure<GeocodingOptions>(builder.Configuration.GetSection(GeocodingOptions.SectionName));
builder.Services.AddHttpClient<NominatimClient>(client =>
{
    client.BaseAddress = NominatimClient.BaseAddress;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(NominatimClient.UserAgent);
});
builder.Services.AddScoped<PlaceNameResolver>();
builder.Services.AddSingleton<PlaceNameSignal>();
builder.Services.AddHostedService<PlaceNameBackgroundService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShoeTrackerContext>();

    // AddShoeOwnership assigns pre-existing shoes to the seeded admin account, so on a
    // database that hasn't reached it yet, migrate only as far as AddUsers, seed, then finish.
    if (!db.Database.GetAppliedMigrations().Any(m => m.EndsWith("_AddShoeOwnership")))
    {
        db.GetService<IMigrator>().Migrate("AddUsers");
    }

    if (!db.Users.Any())
    {
        var seedEmail = builder.Configuration["SeedAdminEmail"];
        var seedPassword = builder.Configuration["SeedAdminPassword"];
        if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword))
        {
            // Plain SQL on purpose: on a fresh or old database this runs with the schema only at
            // AddUsers, so inserting through the EF model would also write columns later
            // migrations add to Users (e.g. DefaultShoeId) and fail.
            db.Database.ExecuteSql($"INSERT INTO Users (Email, PasswordHash) VALUES ({seedEmail}, {PasswordHasher.Hash(seedPassword)})");
            app.Logger.LogInformation("Seeded initial admin user {Email}.", seedEmail);
        }
        else
        {
            app.Logger.LogWarning("No users exist and SeedAdminEmail/SeedAdminPassword are not configured; login is unavailable until seeded.");
        }
    }

    db.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapShoeEndpoints();
app.MapRunEndpoints();
app.MapAuthEndpoints();
app.MapStravaEndpoints();

app.Run();
