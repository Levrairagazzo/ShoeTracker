using ShoeTracker.Api.Models;

namespace ShoeTracker.Api.Services;

public static class MileageCalculator
{
    public static double TotalDistanceKm(IEnumerable<Run> runs) =>
        runs.Sum(r => r.DistanceKm);

    public static bool IsOverThreshold(IEnumerable<Run> runs, double thresholdKm) =>
        TotalDistanceKm(runs) > thresholdKm;
}