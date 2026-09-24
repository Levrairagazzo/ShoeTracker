using System.Net;
using System.Net.Http.Json;
using ShoeTracker.Api.Dtos;

namespace ShoeTracker.Api.Tests;

public class AuthEndpointsTests : ApiTestBase
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsUserAndStartsSession()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(OwnerEmail, Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(OwnerEmail, user!.Email);

        var me = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.Equal(user, me);
    }

    [Theory]
    [InlineData(OwnerEmail, "wrong-password")]
    [InlineData("nobody@test.dev", Password)]
    public async Task Login_WithBadCredentials_Returns401WithoutSession(string email, string password)
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("", Password, "Email")]
    [InlineData("   ", Password, "Email")]
    [InlineData(OwnerEmail, "", "Password")]
    public async Task Login_WithMissingField_ReturnsValidationProblem(string email, string password, string field)
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await ValidationErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var client = await LoginAsync();

        var response = await client.PostAsync("/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/shoes")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/auth/me")]
    [InlineData("POST", "/auth/logout")]
    [InlineData("GET", "/shoes")]
    [InlineData("POST", "/shoes")]
    [InlineData("GET", "/shoes/1")]
    [InlineData("PUT", "/shoes/1")]
    [InlineData("DELETE", "/shoes/1")]
    [InlineData("GET", "/shoes/1/runs")]
    [InlineData("GET", "/runs")]
    [InlineData("GET", "/strava/connect")]
    [InlineData("GET", "/strava/callback")]
    [InlineData("GET", "/strava/status")]
    [InlineData("DELETE", "/strava/connection")]
    [InlineData("POST", "/shoes/1/runs")]
    [InlineData("PUT", "/shoes/1/runs/1")]
    [InlineData("DELETE", "/shoes/1/runs/1")]
    public async Task ProtectedEndpoint_WithoutSession_Returns401(string method, string path)
    {
        var client = Factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
