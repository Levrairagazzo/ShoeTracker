using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Tests;

public class RunLabelsTests
{
    [Theory]
    [InlineData(42.195, false)]
    [InlineData(42.2, true)]
    [InlineData(60.149, true)]
    [InlineData(10, false)]
    public void IsUltra_OnlyBeyondMarathonDistance(double distanceKm, bool expected) =>
        Assert.Equal(expected, RunLabels.IsUltra(distanceKm));
}
