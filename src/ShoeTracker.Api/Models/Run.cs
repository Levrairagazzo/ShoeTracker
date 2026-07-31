namespace ShoeTracker.Api.Models;

public class Run
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public double DistanceKm { get; set; }

    public int ShoeId { get; set; }

    public Shoe? Shoe { get; set; }
}
