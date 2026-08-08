using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Endpoints;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/shoes/{shoeId:int}/runs", async (int shoeId, CreateRunRequest request, ShoeTrackerContext db) =>
        {
            var validation = ValidateRun(request.Date, request.DistanceKm);
            if (validation is not null) return validation;

            var shoeExists = await db.Shoes.AnyAsync(s => s.Id == shoeId);
            if (!shoeExists)
            {
                return Results.NotFound();
            }

            var run = new Run
            {
                ShoeId = shoeId,
                Date = request.Date,
                DistanceKm = request.DistanceKm
            };

            db.Runs.Add(run);
            await db.SaveChangesAsync();

            return Results.Created($"/shoes/{shoeId}/runs/{run.Id}", ToResponse(run));
        }).RequireAuthorization();

        app.MapGet("/shoes/{shoeId:int}/runs", async (int shoeId, ShoeTrackerContext db) =>
        {
            var shoeExists = await db.Shoes.AnyAsync(s => s.Id == shoeId);
            if (!shoeExists)
            {
                return Results.NotFound();
            }

            var runs = await db.Runs
                .Where(r => r.ShoeId == shoeId)
                .OrderByDescending(r => r.Date)
                .ThenByDescending(r => r.Id)
                .ToListAsync();

            return Results.Ok(runs.Select(ToResponse));
        }).RequireAuthorization();

        app.MapPut("/shoes/{shoeId:int}/runs/{id:int}", async (int shoeId, int id, CreateRunRequest request, ShoeTrackerContext db) =>
        {
            var validation = ValidateRun(request.Date, request.DistanceKm);
            if (validation is not null) return validation;

            var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == id && r.ShoeId == shoeId);
            if (run is null) return Results.NotFound();

            run.Date = request.Date;
            run.DistanceKm = request.DistanceKm;

            await db.SaveChangesAsync();

            return Results.Ok(ToResponse(run));
        }).RequireAuthorization();

        app.MapDelete("/shoes/{shoeId:int}/runs/{id:int}", async (int shoeId, int id, ShoeTrackerContext db) =>
        {
            var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == id && r.ShoeId == shoeId);
            if (run is null) return Results.NotFound();

            db.Runs.Remove(run);
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static IResult? ValidateRun(DateOnly date, double distanceKm)
    {
        if (distanceKm <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DistanceKm"] = ["DistanceKm must be greater than zero."]
            });
        }

        if (date > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Date"] = ["Date cannot be in the future."]
            });
        }

        return null;
    }

    private static RunResponse ToResponse(Run run) => new(run.Id, run.Date, run.DistanceKm, run.ShoeId);
}
