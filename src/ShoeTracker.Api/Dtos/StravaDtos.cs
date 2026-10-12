namespace ShoeTracker.Api.Dtos;

/// <param name="Available">Whether the server has Strava API credentials configured at all.</param>
/// <param name="ImportedRuns">How many of the user's runs came from Strava.</param>
/// <param name="UnassignedRuns">How many of the user's runs aren't assigned to a shoe.</param>
public record StravaStatusResponse(bool Available, bool Connected, string? AthleteName, int ImportedRuns, int UnassignedRuns);

public record StravaImportResponse(int Imported);
