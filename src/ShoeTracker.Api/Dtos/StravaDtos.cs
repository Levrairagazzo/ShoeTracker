namespace ShoeTracker.Api.Dtos;

/// <param name="Available">Whether the server has Strava API credentials configured at all.</param>
public record StravaStatusResponse(bool Available, bool Connected, string? AthleteName);
