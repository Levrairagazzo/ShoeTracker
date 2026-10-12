using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services.Strava;

namespace ShoeTracker.Api.Tests;

public class StravaWebhookTests : ApiTestBase
{
    private const string CallbackUrl = "https://example.test/api/strava/webhook";

    /// <summary>Logs in, connects Strava and runs the (empty) history import, so webhook events apply.</summary>
    private async Task<HttpClient> ConnectWithHistoryAsync()
    {
        var client = await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);
        Strava.Respond = _ => FakeHttpHandler.Json(Array.Empty<object>());
        (await client.PostAsync("/strava/import", null)).EnsureSuccessStatusCode();
        Strava.Requests.Clear();
        return client;
    }

    /// <summary>Answers <c>/activities/{id}</c> with <paramref name="activity"/>, or 404 when it's null.</summary>
    private void ServeActivity(long id, object? activity)
    {
        Strava.Respond = request => request.Path == $"/api/v3/activities/{id}"
            ? activity is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : FakeHttpHandler.Json(activity)
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }

    private async Task<bool> HandleAsync(StravaWebhookEvent webhookEvent)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StravaWebhookHandler>().HandleAsync(webhookEvent);
    }

    private static StravaWebhookEvent ActivityEvent(long activityId, string aspectType = "create", long ownerId = StravaAthleteId) =>
        new("activity", activityId, aspectType, ownerId, SubscriptionId: 1, EventTime: 1_700_000_000);

    private static StravaWebhookEvent DeauthorizationEvent() =>
        new("athlete", StravaAthleteId, "update", StravaAthleteId, SubscriptionId: 1, EventTime: 1_700_000_000,
            Updates: new Dictionary<string, JsonElement> { ["authorized"] = JsonSerializer.SerializeToElement("false") });

    private List<Run> StravaRuns()
    {
        var runs = new List<Run>();
        WithDb(db => runs = db.Runs.Where(r => r.Source == RunSource.Strava).OrderBy(r => r.StravaActivityId).ToList());
        return runs;
    }

    private bool IsConnected()
    {
        var connected = false;
        WithDb(db => connected = db.StravaConnections.Any());
        return connected;
    }

    [Fact]
    public async Task Validation_EchoesTheChallenge_WhenTheVerifyTokenMatches()
    {
        var verifyToken = Factory.Services.GetRequiredService<StravaWebhookSubscriber>().VerifyToken;
        var client = Factory.CreateClient();

        var response = await client.GetAsync($"/strava/webhook?hub.mode=subscribe&hub.challenge=abc123&hub.verify_token={verifyToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("abc123", body!["hub.challenge"]);
    }

    [Theory]
    [InlineData("hub.mode=subscribe&hub.challenge=abc123&hub.verify_token=wrong")]
    [InlineData("hub.challenge=abc123")]
    public async Task Validation_WithoutTheVerifyToken_Returns403(string query)
    {
        var response = await Factory.CreateClient().GetAsync($"/strava/webhook?{query}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostedEvent_IsAcceptedWithoutALogin_AndImportsTheRunInTheBackground()
    {
        await ConnectWithHistoryAsync();
        ServeActivity(501, FakeHttpHandler.Activity(501, 10_000, "2026-03-05T07:00:00Z"));

        var response = await Factory.CreateClient().PostAsJsonAsync("/strava/webhook", ActivityEvent(501));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (StravaRuns().Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(50);
        var run = Assert.Single(StravaRuns());
        Assert.Equal(501, run.StravaActivityId);
        Assert.Equal(10, run.DistanceKm);
    }

    [Fact]
    public async Task NewRun_GoesToTheDefaultShoe()
    {
        var client = await ConnectWithHistoryAsync();
        var shoe = await CreateShoeAsync(client);
        await SetDefaultShoeAsync(client, shoe.Id);
        ServeActivity(501, FakeHttpHandler.Activity(501, 8_000, "2026-03-05T07:00:00Z", sportType: "TrailRun"));

        var runsChanged = await HandleAsync(ActivityEvent(501));

        Assert.True(runsChanged);
        var run = Assert.Single(StravaRuns());
        Assert.Equal(shoe.Id, run.ShoeId);
        Assert.Equal(RunType.Trail, run.Type);
        Assert.Equal(new DateOnly(2026, 3, 5), run.Date);
        Assert.Equal("Bearer access-token", Assert.Single(Strava.Requests).Authorization);
    }

    [Fact]
    public async Task Event_BeforeTheHistoryIsImported_IsIgnored()
    {
        await LoginAsync();
        await ConnectStravaAsync(OwnerEmail);

        await HandleAsync(ActivityEvent(501));

        Assert.Empty(StravaRuns());
        Assert.Empty(Strava.Requests);
    }

    [Fact]
    public async Task UpdatedActivity_TakesStravasValues_AndKeepsItsShoe()
    {
        var client = await ConnectWithHistoryAsync();
        var shoe = await CreateShoeAsync(client);
        var runId = AddStravaRun(OwnerEmail, 5, shoeId: shoe.Id, activityId: 501);
        ServeActivity(501, FakeHttpHandler.Activity(501, 6_500, "2026-03-05T07:00:00Z", workoutType: StravaImporter.RaceWorkoutType));

        await HandleAsync(ActivityEvent(501, "update"));

        var run = Assert.Single(StravaRuns());
        Assert.Equal(runId, run.Id);
        Assert.Equal(shoe.Id, run.ShoeId);
        Assert.Equal(6.5, run.DistanceKm);
        Assert.True(run.IsRace);
    }

    [Fact]
    public async Task DeletedActivity_RemovesTheRun()
    {
        await ConnectWithHistoryAsync();
        AddStravaRun(OwnerEmail, 5, activityId: 501);
        ServeActivity(501, null);

        await HandleAsync(ActivityEvent(501, "delete"));

        Assert.Empty(StravaRuns());
    }

    [Fact]
    public async Task ActivityChangedToANonRun_RemovesTheRun()
    {
        await ConnectWithHistoryAsync();
        AddStravaRun(OwnerEmail, 5, activityId: 501);
        ServeActivity(501, FakeHttpHandler.Activity(501, 30_000, "2026-03-05T07:00:00Z", sportType: "Ride"));

        await HandleAsync(ActivityEvent(501, "update"));

        Assert.Empty(StravaRuns());
    }

    [Fact]
    public async Task NonRunActivity_IsNotImported()
    {
        await ConnectWithHistoryAsync();
        ServeActivity(501, FakeHttpHandler.Activity(501, 30_000, "2026-03-05T07:00:00Z", sportType: "Ride"));

        var runsChanged = await HandleAsync(ActivityEvent(501));

        Assert.False(runsChanged);
        Assert.Empty(StravaRuns());
    }

    [Fact]
    public async Task AnotherAthletesActivity_IsIgnored()
    {
        // A forged event can point at someone else's public activity, which Strava would return.
        await ConnectWithHistoryAsync();
        ServeActivity(501, FakeHttpHandler.Activity(501, 10_000, "2026-03-05T07:00:00Z", athleteId: 999));

        await HandleAsync(ActivityEvent(501));

        Assert.Empty(StravaRuns());
    }

    [Fact]
    public async Task EventForAnAthleteWithNoConnection_DoesNothing()
    {
        await ConnectWithHistoryAsync();

        await HandleAsync(ActivityEvent(501, ownerId: 999));

        Assert.Empty(StravaRuns());
        Assert.Empty(Strava.Requests);
    }

    [Fact]
    public async Task Deauthorization_ConfirmedByStrava_RemovesTheConnection_AndKeepsTheRuns()
    {
        await ConnectWithHistoryAsync();
        AddStravaRun(OwnerEmail, 5);
        Strava.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        await HandleAsync(DeauthorizationEvent());

        Assert.False(IsConnected());
        Assert.Single(StravaRuns());
    }

    [Fact]
    public async Task Deauthorization_ThatStravaDoesNotConfirm_KeepsTheConnection()
    {
        await ConnectWithHistoryAsync();
        Strava.Respond = request => request.Path == "/api/v3/athlete"
            ? FakeHttpHandler.Json(new { id = StravaAthleteId, firstname = "Test", lastname = "Runner" })
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);

        await HandleAsync(DeauthorizationEvent());

        Assert.True(IsConnected());
    }

    private StravaWebhookSubscriber Subscriber() => new(
        Factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new StravaOptions
        {
            ClientId = StravaClientId,
            ClientSecret = StravaClientSecret,
            RedirectUri = StravaRedirectUri,
            WebhookCallbackUrl = CallbackUrl
        }),
        Factory.Services.GetRequiredService<IHostApplicationLifetime>(),
        NullLogger<StravaWebhookSubscriber>.Instance);

    [Fact]
    public async Task Subscriber_WithNoSubscription_CreatesOneWithItsVerifyToken()
    {
        var subscriber = Subscriber();
        Strava.Respond = request => request.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(Array.Empty<object>())
            : FakeHttpHandler.Json(new { id = 77 });

        await subscriber.EnsureSubscriptionAsync();

        var create = Assert.Single(Strava.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("/api/v3/push_subscriptions", create.Path);
        Assert.Equal(CallbackUrl, create.Form["callback_url"]);
        Assert.Equal(subscriber.VerifyToken, create.Form["verify_token"]);
        Assert.Equal(StravaClientId, create.Form["client_id"]);
    }

    [Theory]
    [InlineData(CallbackUrl)]
    [InlineData("https://other.test/api/strava/webhook")]
    public async Task Subscriber_WithAnExistingSubscription_LeavesItAlone(string existingUrl)
    {
        Strava.Respond = _ => FakeHttpHandler.Json(new[] { new { id = 77, callback_url = existingUrl } });

        await Subscriber().EnsureSubscriptionAsync();

        var request = Assert.Single(Strava.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }
}
