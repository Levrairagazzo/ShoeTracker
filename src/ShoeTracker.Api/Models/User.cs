namespace ShoeTracker.Api.Models;

public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>The shoe newly imported Strava runs are assigned to; null leaves them unassigned.</summary>
    public int? DefaultShoeId { get; set; }

    public Shoe? DefaultShoe { get; set; }
}
