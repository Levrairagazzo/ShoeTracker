namespace ShoeTracker.Api.Services;

public static class RunLabels
{
    public const double MarathonKm = 42.195;

    /// <summary>Anything longer than a marathon counts as an ultra.</summary>
    public static bool IsUltra(double distanceKm) => distanceKm > MarathonKm;
}
