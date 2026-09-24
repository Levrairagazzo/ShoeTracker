using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;
using ShoeTracker.Api.Services.Geocoding;

namespace ShoeTracker.Api.Endpoints;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/shoes/{shoeId:int}/runs", async (int shoeId, CreateRunRequest request, ShoeTrackerContext db, ClaimsPrincipal user, PlaceNameResolver places) =>
        {
            var validation = ValidateRun(request.Date, request.DistanceKm);
            if (validation is not null) return validation;

            var userId = user.GetUserId();
            var shoeExists = await db.Shoes.AnyAsync(s => s.Id == shoeId && s.UserId == userId);
            if (!shoeExists)
            {
                return Results.NotFound();
            }

            var run = new Run
            {
                UserId = userId,
                ShoeId = shoeId,
                Source = RunSource.Manual,
                Date = request.Date,
                DistanceKm = request.DistanceKm
            };

            db.Runs.Add(run);
            await db.SaveChangesAsync();

            return Results.Created($"/shoes/{shoeId}/runs/{run.Id}", await ToResponseAsync(run, places));
        }).RequireAuthorization();

        app.MapGet("/shoes/{shoeId:int}/runs", async (int shoeId, ShoeTrackerContext db, ClaimsPrincipal user, PlaceNameResolver places) =>
        {
            var userId = user.GetUserId();
            var shoeExists = await db.Shoes.AnyAsync(s => s.Id == shoeId && s.UserId == userId);
            if (!shoeExists)
            {
                return Results.NotFound();
            }

            var runs = await db.Runs
                .Where(r => r.ShoeId == shoeId && r.UserId == userId)
                .OrderByDescending(r => r.Date)
                .ThenByDescending(r => r.Id)
                .ToListAsync();

            return Results.Ok(await ToResponsesAsync(runs, places));
        }).RequireAuthorization();

        app.MapGet("/runs", async (bool? unassigned, RunSource? source, int? limit, ShoeTrackerContext db, ClaimsPrincipal user, PlaceNameResolver places) =>
        {
            if (limit <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = ["limit must be greater than zero."]
                });
            }

            var userId = user.GetUserId();
            var query = db.Runs.Where(r => r.UserId == userId);
            if (unassigned == true)
            {
                query = query.Where(r => r.ShoeId == null);
            }

            if (source is not null)
            {
                query = query.Where(r => r.Source == source);
            }

            var ordered = query
                .OrderByDescending(r => r.Date)
                .ThenByDescending(r => r.Id);
            var runs = await (limit is null ? ordered : ordered.Take(limit.Value)).ToListAsync();

            return Results.Ok(await ToResponsesAsync(runs, places));
        }).RequireAuthorization();

        app.MapPut("/shoes/{shoeId:int}/runs/{id:int}", async (int shoeId, int id, CreateRunRequest request, ShoeTrackerContext db, ClaimsPrincipal user, PlaceNameResolver places) =>
        {
            var validation = ValidateRun(request.Date, request.DistanceKm);
            if (validation is not null) return validation;

            var userId = user.GetUserId();
            var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == id && r.ShoeId == shoeId && r.UserId == userId);
            if (run is null) return Results.NotFound();

            run.Date = request.Date;
            run.DistanceKm = request.DistanceKm;

            await db.SaveChangesAsync();

            return Results.Ok(await ToResponseAsync(run, places));
        }).RequireAuthorization();

        app.MapDelete("/shoes/{shoeId:int}/runs/{id:int}", async (int shoeId, int id, ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == id && r.ShoeId == shoeId && r.UserId == userId);
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

    private static async Task<RunResponse> ToResponseAsync(Run run, PlaceNameResolver places) =>
        (await ToResponsesAsync([run], places))[0];

    private static async Task<List<RunResponse>> ToResponsesAsync(IReadOnlyCollection<Run> runs, PlaceNameResolver places)
    {
        var names = await places.NamesForAsync(runs);
        return runs.Select(run => new RunResponse(
            run.Id,
            run.Date,
            run.DistanceKm,
            run.ShoeId,
            run.Source,
            run.Type,
            run.IsRace,
            RunLabels.IsUltra(run.DistanceKm),
            names.GetValueOrDefault(run.Id))).ToList();
    }
}
