using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Dtos;

public record CreateRunRequest(DateOnly Date, double DistanceKm);

public record RunResponse(int Id, DateOnly Date, double DistanceKm, int? ShoeId, RunSource Source);
