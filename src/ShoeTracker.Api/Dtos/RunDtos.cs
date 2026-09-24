using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Dtos;

public record CreateRunRequest(DateOnly Date, double DistanceKm);

/// <param name="Type">Null for manual runs.</param>
/// <param name="IsUltra">Longer than a marathon.</param>
/// <param name="Location">Where the run started, e.g. "Oakland, California"; null if unknown or not looked up yet.</param>
public record RunResponse(
    int Id,
    DateOnly Date,
    double DistanceKm,
    int? ShoeId,
    RunSource Source,
    RunType? Type,
    bool IsRace,
    bool IsUltra,
    string? Location);
