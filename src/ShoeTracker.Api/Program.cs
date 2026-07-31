using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShoeTrackerContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ShoeTrackerContext")
        ?? throw new InvalidOperationException("Connection string 'ShoeTrackerContext' not found.")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShoeTrackerContext>();
    db.Database.Migrate();
}

app.MapShoeEndpoints();
app.MapRunEndpoints();

app.Run();
