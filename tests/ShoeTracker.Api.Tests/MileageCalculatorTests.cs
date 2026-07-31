using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Tests;

public class MileageCalculatorTests
{
    private static Run RunOf(double distanceKm) => new() { DistanceKm = distanceKm };

    [Fact]
    public void TotalDistanceKm_WithNoRuns_ReturnsZero()
    {
        var total = MileageCalculator.TotalDistanceKm([]);

        Assert.Equal(0, total);
    }

    [Fact]
    public void TotalDistanceKm_WithSingleRun_ReturnsThatDistance()
    {
        var total = MileageCalculator.TotalDistanceKm([RunOf(10.5)]);

        Assert.Equal(10.5, total);
    }

    [Fact]
    public void TotalDistanceKm_WithMultipleRuns_SumsAllDistances()
    {
        var runs = new[] { RunOf(650), RunOf(100), RunOf(25.5) };

        var total = MileageCalculator.TotalDistanceKm(runs);

        Assert.Equal(775.5, total);
    }

    [Fact]
    public void IsOverThreshold_WhenTotalBelowThreshold_ReturnsFalse()
    {
        var runs = new[] { RunOf(300), RunOf(200) };

        var result = MileageCalculator.IsOverThreshold(runs, 700);

        Assert.False(result);
    }

    [Fact]
    public void IsOverThreshold_WhenTotalExactlyAtThreshold_ReturnsFalse()
    {
        var runs = new[] { RunOf(700) };

        var result = MileageCalculator.IsOverThreshold(runs, 700);

        Assert.False(result);
    }

    [Fact]
    public void IsOverThreshold_WhenTotalExceedsThreshold_ReturnsTrue()
    {
        var runs = new[] { RunOf(650), RunOf(100) };

        var result = MileageCalculator.IsOverThreshold(runs, 700);

        Assert.True(result);
    }

    [Fact]
    public void IsOverThreshold_RespectsCustomThresholdPerShoe()
    {
        var runs = new[] { RunOf(400) };

        Assert.True(MileageCalculator.IsOverThreshold(runs, 300));
        Assert.False(MileageCalculator.IsOverThreshold(runs, 500));
    }
}
