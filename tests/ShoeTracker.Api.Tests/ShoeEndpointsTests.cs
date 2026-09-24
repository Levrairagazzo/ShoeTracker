using System.Net;
using System.Net.Http.Json;
using ShoeTracker.Api.Dtos;

namespace ShoeTracker.Api.Tests;

public class ShoeEndpointsTests : ApiTestBase
{
    [Fact]
    public async Task Create_ReturnsCreatedShoeWithDefaultThresholdAndNoMileage()
    {
        var client = await LoginAsync();

        var response = await client.PostAsJsonAsync("/shoes", new CreateShoeRequest("Pegasus", "Nike", new DateOnly(2026, 1, 1), null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var shoe = await response.Content.ReadFromJsonAsync<ShoeResponse>();
        Assert.Equal($"/shoes/{shoe!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(new ShoeResponse(shoe.Id, "Pegasus", "Nike", new DateOnly(2026, 1, 1), 700, false, 0, false, false), shoe);
    }

    [Fact]
    public async Task Create_WithCustomThreshold_UsesIt()
    {
        var client = await LoginAsync();

        var shoe = await CreateShoeAsync(client, thresholdKm: 500);

        Assert.Equal(500, shoe.ThresholdKm);
    }

    [Theory]
    [InlineData("", "Nike", null, "Name")]
    [InlineData("  ", "Nike", null, "Name")]
    [InlineData("Pegasus", "", null, "Brand")]
    [InlineData("Pegasus", "Nike", 0.0, "ThresholdKm")]
    [InlineData("Pegasus", "Nike", -100.0, "ThresholdKm")]
    public async Task CreateAndUpdate_WithInvalidFields_ReturnValidationProblem(string name, string brand, double? thresholdKm, string field)
    {
        var client = await LoginAsync();
        var existing = await CreateShoeAsync(client);
        var request = new CreateShoeRequest(name, brand, new DateOnly(2026, 1, 1), thresholdKm);

        var create = await client.PostAsJsonAsync("/shoes", request);
        var update = await client.PutAsJsonAsync($"/shoes/{existing.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal([field], await ValidationErrorFieldsAsync(create));
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal([field], await ValidationErrorFieldsAsync(update));
        Assert.Equal([existing.Id], (await client.GetFromJsonAsync<List<ShoeResponse>>("/shoes"))!.Select(s => s.Id));
    }

    [Fact]
    public async Task Get_ReportsMileageAndOverThresholdFromLoggedRuns()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client, thresholdKm: 20);

        await LogRunAsync(client, shoe.Id, 12.5);
        await LogRunAsync(client, shoe.Id, 7.5);
        var atThreshold = await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}");

        await LogRunAsync(client, shoe.Id, 0.1);
        var overThreshold = await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}");
        var listed = Assert.Single((await client.GetFromJsonAsync<List<ShoeResponse>>("/shoes"))!);

        Assert.Equal(20, atThreshold!.TotalDistanceKm);
        Assert.False(atThreshold.IsOverThreshold);
        Assert.Equal(20.1, overThreshold!.TotalDistanceKm, precision: 6);
        Assert.True(overThreshold.IsOverThreshold);
        Assert.Equal(overThreshold, listed);
    }

    [Fact]
    public async Task Update_ChangesFieldsAndRecomputesThresholdStatus()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        await LogRunAsync(client, shoe.Id, 50);

        var response = await client.PutAsJsonAsync($"/shoes/{shoe.Id}", new CreateShoeRequest("Vaporfly", "Nike", new DateOnly(2026, 2, 1), 40));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = new ShoeResponse(shoe.Id, "Vaporfly", "Nike", new DateOnly(2026, 2, 1), 40, false, 50, true, false);
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<ShoeResponse>());
        Assert.Equal(expected, await client.GetFromJsonAsync<ShoeResponse>($"/shoes/{shoe.Id}"));
    }

    [Fact]
    public async Task Delete_RemovesShoeAndCascadesToItsRuns()
    {
        var client = await LoginAsync();
        var shoe = await CreateShoeAsync(client);
        var keptShoe = await CreateShoeAsync(client);
        await LogRunAsync(client, shoe.Id, 5);
        await LogRunAsync(client, shoe.Id, 6);
        await LogRunAsync(client, keptShoe.Id, 7);

        var response = await client.DeleteAsync($"/shoes/{shoe.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/shoes/{shoe.Id}")).StatusCode);
        WithDb(db =>
        {
            Assert.DoesNotContain(db.Runs, r => r.ShoeId == shoe.Id);
            Assert.Single(db.Runs, r => r.ShoeId == keptShoe.Id);
        });
    }

    [Fact]
    public async Task MissingShoe_Returns404()
    {
        var client = await LoginAsync();
        var request = new CreateShoeRequest("Pegasus", "Nike", new DateOnly(2026, 1, 1), null);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/shoes/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/shoes/999", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/shoes/999")).StatusCode);
    }
}
