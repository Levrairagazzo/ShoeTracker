using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services.Geocoding;

namespace ShoeTracker.Api.Tests;

public class PlaceNameTests : ApiTestBase
{
    private void AddRunStartingAt(double latitude, double longitude, DateOnly date) => WithDb(db =>
    {
        db.Runs.Add(new Run
        {
            UserId = db.Users.Single(u => u.Email == OwnerEmail).Id,
            Source = RunSource.Strava,
            Type = RunType.Run,
            Date = date,
            DistanceKm = 5,
            StartLatitude = latitude,
            StartLongitude = longitude
        });
        db.SaveChanges();
    });

    private async Task<int> ResolvePendingAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PlaceNameResolver>().ResolvePendingAsync();
    }

    /// <summary>Answers reverse lookups with Oakland for latitudes near 37.8 and Golden elsewhere.</summary>
    private void ServePlaces() => Nominatim.Respond = request =>
        FakeHttpHandler.Json(new
        {
            address = request.Query["lat"].StartsWith("37.8")
                ? new { city = "Oakland", state = "California", country = "United States", country_code = "us" }
                : new { city = (string?)null, state = "British Columbia", country = "Canada", country_code = "ca", town = "Golden" } as object
        });

    [Fact]
    public async Task ResolvePending_LooksUpEachAreaOnceUsingTheRoundedAreaCentre()
    {
        AddRunStartingAt(37.80441, -122.27123, new DateOnly(2026, 3, 1));
        AddRunStartingAt(37.80389, -122.27049, new DateOnly(2026, 3, 2)); // same ~1 km area
        AddRunStartingAt(51.29913, -116.96335, new DateOnly(2026, 3, 3));
        ServePlaces();

        var resolved = await ResolvePendingAsync();

        Assert.Equal(2, resolved);
        Assert.Equal(
            [("37.8", "-122.27"), ("51.3", "-116.96")],
            Nominatim.Requests.Select(r => (r.Query["lat"], r.Query["lon"])).Order());
        Assert.All(Nominatim.Requests, r => Assert.Equal(NominatimClient.UserAgent, r.UserAgent));

        var client = await LoginAsync();
        var runs = await client.GetFromJsonAsync<List<RunResponse>>("/runs");
        Assert.Equal(["Golden, Canada", "Oakland, California", "Oakland, California"], runs!.Select(r => r.Location));
    }

    [Fact]
    public async Task ResolvePending_SkipsAreasAlreadyCached()
    {
        AddRunStartingAt(37.8044, -122.2712, new DateOnly(2026, 3, 1));
        ServePlaces();
        await ResolvePendingAsync();
        AddRunStartingAt(37.8041, -122.2709, new DateOnly(2026, 3, 2));
        Nominatim.Requests.Clear();

        var resolved = await ResolvePendingAsync();

        Assert.Equal(0, resolved);
        Assert.Empty(Nominatim.Requests);
    }

    [Fact]
    public async Task ResolvePending_WhenLookupFails_CachesNothingAndRetriesNextPass()
    {
        AddRunStartingAt(37.8044, -122.2712, new DateOnly(2026, 3, 1));
        Nominatim.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var failedPass = await ResolvePendingAsync();
        ServePlaces();
        var retryPass = await ResolvePendingAsync();

        Assert.Equal(0, failedPass);
        Assert.Equal(1, retryPass);
    }

    [Fact]
    public async Task ResolvePending_WhenNoPlaceIsNamed_CachesTheMissAndReturnsNoLocation()
    {
        AddRunStartingAt(0.5, -30.5, new DateOnly(2026, 3, 1)); // mid-Atlantic
        Nominatim.Respond = _ => FakeHttpHandler.Json(new { error = "Unable to geocode" });

        await ResolvePendingAsync();
        var secondPass = await ResolvePendingAsync();

        Assert.Equal(0, secondPass);
        Assert.Single(Nominatim.Requests);
        var client = await LoginAsync();
        Assert.Null(Assert.Single((await client.GetFromJsonAsync<List<RunResponse>>("/runs"))!).Location);
    }

    [Fact]
    public async Task RunsWithoutAStartPoint_HaveNoLocation()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        await LogRunAsync(client, shoe.Id, 5);

        await ResolvePendingAsync();

        Assert.Empty(Nominatim.Requests);
        Assert.Null(Assert.Single((await client.GetFromJsonAsync<List<RunResponse>>("/runs"))!).Location);
    }

    [Theory]
    [InlineData("Oakland", null, "California", "United States", "us", "Oakland, California")]
    [InlineData(null, "Golden", "British Columbia", "Canada", "ca", "Golden, Canada")]
    [InlineData(null, null, null, "France", "fr", "France")]
    public void FormatName_UsesStateInTheUsAndCountryElsewhere(
        string? city, string? town, string? state, string? country, string countryCode, string expected)
    {
        var name = NominatimClient.FormatName(new NominatimClient.Address(
            City: city, Town: town, State: state, Country: country, CountryCode: countryCode));

        Assert.Equal(expected, name);
    }
}
