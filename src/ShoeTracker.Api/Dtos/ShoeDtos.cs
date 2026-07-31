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
    bool IsOverThreshold);
