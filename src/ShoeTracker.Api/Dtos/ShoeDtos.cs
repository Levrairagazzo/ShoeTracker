namespace ShoeTracker.Api.Dtos;

public record CreateShoeRequest(string Name, string Brand, DateOnly PurchaseDate, double? ThresholdKm);

public record ShoeResponse(
    int Id,
    string Name,
    string Brand,
    DateOnly PurchaseDate,
    double ThresholdKm,
    bool IsRetired,
    double TotalDistanceKm,
    bool IsOverThreshold,
    bool IsDefault);

/// <param name="ShoeId">The new default shoe, or null to have no default.</param>
public record SetDefaultShoeRequest(int? ShoeId);
