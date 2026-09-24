using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Endpoints;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;

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
            db.Users.Add(new User { Email = seedEmail, PasswordHash = PasswordHasher.Hash(seedPassword) });
            db.SaveChanges();
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

app.Run();
