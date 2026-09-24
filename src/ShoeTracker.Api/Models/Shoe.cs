namespace ShoeTracker.Api.Models;

public class Shoe
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public DateOnly PurchaseDate { get; set; }

    public double ThresholdKm { get; set; } = 700;

    public bool IsRetired { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public List<Run> Runs { get; set; } = [];
}
