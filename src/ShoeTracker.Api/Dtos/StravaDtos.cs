namespace ShoeTracker.Api.Dtos;

/// <param name="Available">Whether the server has Strava API credentials configured at all.</param>
/// <param name="ImportedRuns">How many of the user's runs came from Strava.</param>
public record StravaStatusResponse(bool Available, bool Connected, string? AthleteName, int ImportedRuns);

public record StravaImportResponse(int Imported);
