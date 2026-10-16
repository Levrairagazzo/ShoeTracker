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
        app.MapPost("/shoes/{shoeId:int}/runs", async (int shoeId, CreateRunRequest request, TrainingLog log, ClaimsPrincipal user, PlaceNameResolver places) =>
        {
            var result = await log.LogRunAsync(user.GetUserId(), shoeId, request.Date, request.DistanceKm);
            return await result.ToResultAsync<Run>(async run => Results.Created($"/runs/{run.Id}", await ToResponseAsync(run, places)));
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

        // A run is addressed by its own id, not through a shoe: an unassigned run stays editable.
        app.MapPut("/runs/{id:int}", async (int id, CreateRunRequest request, TrainingLog log, ClaimsPrincipal user, PlaceNameResolver places) =>
        {
            var result = await log.EditRunAsync(user.GetUserId(), id, request.Date, request.DistanceKm);
            return await result.ToResultAsync<Run>(async run => Results.Ok(await ToResponseAsync(run, places)));
        }).RequireAuthorization();

        app.MapDelete("/runs/{id:int}", async (int id, TrainingLog log, ClaimsPrincipal user) =>
            (await log.DeleteRunAsync(user.GetUserId(), id)).ToResult(Results.NoContent())).RequireAuthorization();

        // Moves any of the user's runs (manual or Strava) to another shoe, or unassigns it.
        app.MapPut("/runs/{id:int}/shoe", async (int id, AssignRunRequest request, TrainingLog log, ClaimsPrincipal user, PlaceNameResolver places) =>
        {
            var result = await log.MoveRunAsync(user.GetUserId(), id, request.ShoeId);
            return await result.ToResultAsync<Run>(async run => Results.Ok(await ToResponseAsync(run, places)));
        }).RequireAuthorization();
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
