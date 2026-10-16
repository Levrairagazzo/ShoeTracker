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

        group.MapPost("/", async (CreateShoeRequest request, TrainingLog log, ClaimsPrincipal user) =>
        {
            var result = await log.CreateShoeAsync(user.GetUserId(), request.Name, request.Brand, request.PurchaseDate, request.ThresholdKm);
            return result.ToResult<Shoe>(shoe => Results.Created($"/shoes/{shoe.Id}", ToResponse(shoe, defaultShoeId: null)));
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

        group.MapPut("/{id:int}", async (int id, CreateShoeRequest request, TrainingLog log, ShoeTrackerContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            var result = await log.UpdateShoeAsync(userId, id, request.Name, request.Brand, request.PurchaseDate, request.ThresholdKm);
            return await result.ToResultAsync<Shoe>(async shoe => Results.Ok(ToResponse(shoe, await DefaultShoeIdAsync(db, userId))));
        });

        group.MapDelete("/{id:int}", async (int id, TrainingLog log, ClaimsPrincipal user) =>
            (await log.DeleteShoeAsync(user.GetUserId(), id)).ToResult(Results.NoContent()));

        group.MapPut("/default", async (SetDefaultShoeRequest request, TrainingLog log, ClaimsPrincipal user) =>
            (await log.SetDefaultShoeAsync(user.GetUserId(), request.ShoeId)).ToResult(Results.NoContent()));
    }

    private static Task<int?> DefaultShoeIdAsync(ShoeTrackerContext db, int userId) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.DefaultShoeId).SingleAsync();

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
