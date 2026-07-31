using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Endpoints;

public static class ShoeEndpoints
{
    public static void MapShoeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shoes");

        group.MapPost("/", async (CreateShoeRequest request, ShoeTrackerContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Name"] = ["Name is required."]
                });
            }

            if (string.IsNullOrWhiteSpace(request.Brand))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Brand"] = ["Brand is required."]
                });
            }

            var thresholdKm = request.ThresholdKm ?? 700;
            if (thresholdKm <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ThresholdKm"] = ["ThresholdKm must be greater than zero."]
                });
            }

            var shoe = new Shoe
            {
                Name = request.Name,
                Brand = request.Brand,
                PurchaseDate = request.PurchaseDate,
                ThresholdKm = thresholdKm
            };

            db.Shoes.Add(shoe);
            await db.SaveChangesAsync();

            return Results.Created($"/shoes/{shoe.Id}", ToResponse(shoe));
        });

        group.MapGet("/", async (ShoeTrackerContext db) =>
        {
            var shoes = await db.Shoes.Include(s => s.Runs).ToListAsync();
            return Results.Ok(shoes.Select(ToResponse));
        });

        group.MapGet("/{id:int}", async (int id, ShoeTrackerContext db) =>
        {
            var shoe = await db.Shoes.Include(s => s.Runs).FirstOrDefaultAsync(s => s.Id == id);
            return shoe is null ? Results.NotFound() : Results.Ok(ToResponse(shoe));
        });
    }

    private static ShoeResponse ToResponse(Shoe shoe) => new(
        shoe.Id,
        shoe.Name,
        shoe.Brand,
        shoe.PurchaseDate,
        shoe.ThresholdKm,
        shoe.IsRetired,
        MileageCalculator.TotalDistanceKm(shoe.Runs),
        MileageCalculator.IsOverThreshold(shoe.Runs, shoe.ThresholdKm));
}
