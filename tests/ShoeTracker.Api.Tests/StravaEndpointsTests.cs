using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Services.Strava;

namespace ShoeTracker.Api.Tests;

public class StravaEndpointsTests : ApiTestBase
{
    private const string FullScope = "read,activity:read_all";

    public StravaEndpointsTests()
    {
        Strava.Respond = request => request.Path switch
        {
            "/oauth/token" when request.Form["grant_type"] == "authorization_code" =>
                FakeHttpHandler.Json(FakeHttpHandler.TokenBody("access-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(6))),
            "/oauth/deauthorize" => FakeHttpHandler.Json(new { access_token = request.Form["access_token"] }),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
    }

    /// <summary>Starts the connect flow and returns the state Strava would echo back.</summary>
    private static async Task<string> StartConnectAsync(HttpClient client)
    {
        var response = await client.GetAsync("/strava/connect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return QueryHelpers.ParseQuery(response.Headers.Location!.Query)["state"]!;
    }

    private static Task<HttpResponseMessage> CallbackAsync(HttpClient client, string? state, string scope = FullScope, string? error = null)
    {
        var query = new Dictionary<string, string?> { ["state"] = state, ["scope"] = scope };
        if (error is null) query["code"] = "auth-code"; else query["error"] = error;
        return client.GetAsync(QueryHelpers.AddQueryString("/strava/callback", query));
    }

    private async Task ConnectAsync(HttpClient client)
    {
        var response = await CallbackAsync(client, await StartConnectAsync(client));
        Assert.Equal("/?strava=connected", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Connect_RedirectsToStravaAuthorizeWithReadAllScope()
    {
        var client = await LoginAsync();

        var response = await client.GetAsync("/strava/connect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://www.strava.com/oauth/authorize", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(StravaClientId, query["client_id"]);
        Assert.Equal(StravaRedirectUri, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("activity:read_all", query["scope"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
    }

    [Fact]
    public async Task Callback_WithValidState_ExchangesCodeAndStoresEncryptedTokens()
    {
        var client = await LoginAsync();

        await ConnectAsync(client);

        var exchange = Assert.Single(Strava.Requests);
        Assert.Equal("/oauth/token", exchange.Path);
        Assert.Equal("auth-code", exchange.Form["code"]);
        Assert.Equal(StravaClientId, exchange.Form["client_id"]);
        Assert.Equal(StravaClientSecret, exchange.Form["client_secret"]);

        var status = await client.GetFromJsonAsync<StravaStatusResponse>("/strava/status");
        Assert.Equal(new StravaStatusResponse(true, true, "Test Runner", 0, 0), status);

        WithDb(db =>
        {
            var connection = Assert.Single(db.StravaConnections);
            Assert.Equal(12345, connection.AthleteId);
            Assert.Equal(FullScope, connection.Scope);
            Assert.DoesNotContain("access-1", connection.EncryptedAccessToken);
            Assert.DoesNotContain("refresh-1", connection.EncryptedRefreshToken);
        });
    }

    [Fact]
    public async Task Callback_WithWrongOrMissingState_IsRejectedWithoutExchangingTheCode()
    {
        var client = await LoginAsync();
        await StartConnectAsync(client);

        var wrongState = await CallbackAsync(client, "not-the-state");
        var noState = await CallbackAsync(client, null);

        Assert.Equal("/?strava=error", wrongState.Headers.Location?.OriginalString);
        Assert.Equal("/?strava=error", noState.Headers.Location?.OriginalString);
        Assert.Empty(Strava.Requests);
        WithDb(db => Assert.Empty(db.StravaConnections));
    }

    [Fact]
    public async Task Callback_StateIsSingleUse()
    {
        var client = await LoginAsync();
        var state = await StartConnectAsync(client);
        await CallbackAsync(client, state);

        var replay = await CallbackAsync(client, state);

        Assert.Equal("/?strava=error", replay.Headers.Location?.OriginalString);
        Assert.Single(Strava.Requests);
    }

    [Fact]
    public async Task Callback_WhenUserDeniesAccess_RedirectsWithDenied()
    {
        var client = await LoginAsync();

        var response = await CallbackAsync(client, await StartConnectAsync(client), scope: "", error: "access_denied");

        Assert.Equal("/?strava=denied", response.Headers.Location?.OriginalString);
        Assert.Empty(Strava.Requests);
    }

    [Fact]
    public async Task Callback_WithoutActivityReadAllScope_IsRejected()
    {
        var client = await LoginAsync();

        var response = await CallbackAsync(client, await StartConnectAsync(client), scope: "read,activity:read");

        Assert.Equal("/?strava=missing-scope", response.Headers.Location?.OriginalString);
        Assert.Empty(Strava.Requests);
        WithDb(db => Assert.Empty(db.StravaConnections));
    }

    [Fact]
    public async Task Callback_WhenTokenExchangeFails_RedirectsWithError()
    {
        var client = await LoginAsync();
        Strava.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest);

        var response = await CallbackAsync(client, await StartConnectAsync(client));

        Assert.Equal("/?strava=error", response.Headers.Location?.OriginalString);
        WithDb(db => Assert.Empty(db.StravaConnections));
    }

    [Fact]
    public async Task Status_WhenNotConnected_ReportsAvailableButNotConnected()
    {
        var client = await LoginAsync();

        var status = await client.GetFromJsonAsync<StravaStatusResponse>("/strava/status");

        Assert.Equal(new StravaStatusResponse(true, false, null, 0, 0), status);
    }

    [Fact]
    public async Task WithoutCredentialsConfigured_StatusIsUnavailableAndConnectReturns503()
    {
        using var factory = Factory.WithWebHostBuilder(b => b.UseSetting("Strava:ClientSecret", ""));
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await client.PostAsJsonAsync("/auth/login", new LoginRequest(OwnerEmail, Password));

        var status = await client.GetFromJsonAsync<StravaStatusResponse>("/strava/status");
        var connect = await client.GetAsync("/strava/connect");

        Assert.False(status!.Available);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, connect.StatusCode);
    }

    [Fact]
    public async Task Disconnect_RevokesOnStravaAndDeletesTheConnection()
    {
        var client = await LoginAsync();
        await ConnectAsync(client);

        var response = await client.DeleteAsync("/strava/connection");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var deauthorize = Assert.Single(Strava.Requests, r => r.Path == "/oauth/deauthorize");
        Assert.Equal("access-1", deauthorize.Form["access_token"]);
        Assert.False((await client.GetFromJsonAsync<StravaStatusResponse>("/strava/status"))!.Connected);
    }

    [Fact]
    public async Task Disconnect_WhenStravaRevokeFails_StillDeletesTheConnection()
    {
        var client = await LoginAsync();
        await ConnectAsync(client);
        Strava.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var response = await client.DeleteAsync("/strava/connection");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        WithDb(db => Assert.Empty(db.StravaConnections));
    }

    [Fact]
    public async Task Connection_IsScopedToTheUser()
    {
        var owner = await LoginAsync(OwnerEmail);
        var other = await LoginAsync(OtherEmail);
        await ConnectAsync(owner);

        var otherStatus = await other.GetFromJsonAsync<StravaStatusResponse>("/strava/status");
        await other.DeleteAsync("/strava/connection");

        Assert.False(otherStatus!.Connected);
        Assert.True((await owner.GetFromJsonAsync<StravaStatusResponse>("/strava/status"))!.Connected);
        Assert.DoesNotContain(Strava.Requests, r => r.Path == "/oauth/deauthorize");
    }

    [Fact]
    public async Task TokenStore_ReturnsStoredTokenWhileValid_AndRefreshesWhenNearExpiry()
    {
        var client = await LoginAsync();
        await ConnectAsync(client);
        var userId = 0;
        WithDb(db => userId = db.Users.Single(u => u.Email == OwnerEmail).Id);

        var stillValid = await WithTokenStore(store => store.GetValidAccessTokenAsync(userId));

        WithDb(db =>
        {
            db.StravaConnections.Single().ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
            db.SaveChanges();
        });
        Strava.Respond = request => request.Form.GetValueOrDefault("grant_type") == "refresh_token"
            ? FakeHttpHandler.Json(FakeHttpHandler.TokenBody("access-2", "refresh-2", DateTimeOffset.UtcNow.AddHours(6), withAthlete: false))
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var refreshed = await WithTokenStore(store => store.GetValidAccessTokenAsync(userId));
        var afterRefresh = await WithTokenStore(store => store.GetValidAccessTokenAsync(userId));

        Assert.Equal("access-1", stillValid);
        Assert.Equal("access-2", refreshed);
        Assert.Equal("access-2", afterRefresh);
        var refresh = Assert.Single(Strava.Requests, r => r.Form.GetValueOrDefault("grant_type") == "refresh_token");
        Assert.Equal("refresh-1", refresh.Form["refresh_token"]);
        WithDb(db => Assert.Equal("Test Runner", db.StravaConnections.Single().AthleteName));
    }

    private async Task<T> WithTokenStore<T>(Func<StravaTokenStore, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<StravaTokenStore>());
    }
}
