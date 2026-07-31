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
            if (request.DistanceKm <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["DistanceKm"] = ["DistanceKm must be greater than zero."]
                });
            }

            if (request.Date > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Date"] = ["Date cannot be in the future."]
                });
            }

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

            return Results.Created($"/shoes/{shoeId}/runs/{run.Id}",
                new RunResponse(run.Id, run.Date, run.DistanceKm, run.ShoeId));
        });
    }
}
