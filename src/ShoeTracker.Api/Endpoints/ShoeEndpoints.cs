using System.Security.Claims;
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
        var group = app.MapGroup("/shoes").RequireAuthorization();

        group.MapPost("/", async (CreateShoeRequest request, ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var thresholdKm = request.ThresholdKm ?? 700;
            var validation = ValidateShoe(request.Name, request.Brand, thresholdKm);
            if (validation is not null) return validation;

            var shoe = new Shoe
            {
                Name = request.Name,
                Brand = request.Brand,
                PurchaseDate = request.PurchaseDate,
                ThresholdKm = thresholdKm,
                UserId = user.GetUserId()
            };

            db.Shoes.Add(shoe);
            await db.SaveChangesAsync();

            return Results.Created($"/shoes/{shoe.Id}", ToResponse(shoe, defaultShoeId: null));
        });

        group.MapGet("/", async (ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            var shoes = await db.Shoes.Include(s => s.Runs).Where(s => s.UserId == userId).ToListAsync();
            var defaultShoeId = await DefaultShoeIdAsync(db, userId);
            return Results.Ok(shoes.Select(s => ToResponse(s, defaultShoeId)));
        });

        group.MapGet("/{id:int}", async (int id, ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            var shoe = await db.Shoes.Include(s => s.Runs).FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            return shoe is null ? Results.NotFound() : Results.Ok(ToResponse(shoe, await DefaultShoeIdAsync(db, userId)));
        });

        group.MapPut("/{id:int}", async (int id, CreateShoeRequest request, ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var thresholdKm = request.ThresholdKm ?? 700;
            var validation = ValidateShoe(request.Name, request.Brand, thresholdKm);
            if (validation is not null) return validation;

            var userId = user.GetUserId();
            var shoe = await db.Shoes.Include(s => s.Runs).FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (shoe is null) return Results.NotFound();

            shoe.Name = request.Name;
            shoe.Brand = request.Brand;
            shoe.PurchaseDate = request.PurchaseDate;
            shoe.ThresholdKm = thresholdKm;

            await db.SaveChangesAsync();

            return Results.Ok(ToResponse(shoe, await DefaultShoeIdAsync(db, userId)));
        });

        group.MapDelete("/{id:int}", async (int id, ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            var shoe = await db.Shoes.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (shoe is null) return Results.NotFound();

            // Strava runs belong to Strava, so they're unassigned rather than deleted with the
            // shoe; manual runs cascade-delete as before.
            await db.Runs
                .Where(r => r.ShoeId == id && r.Source == RunSource.Strava)
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.ShoeId, (int?)null));

            db.Shoes.Remove(shoe);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        group.MapPut("/default", async (SetDefaultShoeRequest request, ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            if (request.ShoeId is { } shoeId && !await db.Shoes.AnyAsync(s => s.Id == shoeId && s.UserId == userId))
            {
                return Results.NotFound();
            }

            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DefaultShoeId, request.ShoeId));

            return Results.NoContent();
        });
    }

    private static Task<int?> DefaultShoeIdAsync(ShoeTrackerContext db, int userId) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.DefaultShoeId).SingleAsync();

    private static IResult? ValidateShoe(string name, string brand, double thresholdKm)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = ["Name is required."]
            });
        }

        if (string.IsNullOrWhiteSpace(brand))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Brand"] = ["Brand is required."]
            });
        }

        if (thresholdKm <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ThresholdKm"] = ["ThresholdKm must be greater than zero."]
            });
        }

        return null;
    }

    private static ShoeResponse ToResponse(Shoe shoe, int? defaultShoeId) => new(
        shoe.Id,
        shoe.Name,
        shoe.Brand,
        shoe.PurchaseDate,
        shoe.ThresholdKm,
        shoe.IsRetired,
        MileageCalculator.TotalDistanceKm(shoe.Runs),
        MileageCalculator.IsOverThreshold(shoe.Runs, shoe.ThresholdKm),
        shoe.Id == defaultShoeId);
}
