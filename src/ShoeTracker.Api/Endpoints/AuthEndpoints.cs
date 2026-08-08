using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ShoeTracker.Api.Data;
using ShoeTracker.Api.Dtos;
using ShoeTracker.Api.Models;
using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", async (LoginRequest request, ShoeTrackerContext db, HttpContext http) =>
        {
            var validation = ValidateLogin(request.Email, request.Password);
            if (validation is not null) return validation;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email)
                ],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(ToResponse(user));
        });

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var id = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var email = principal.FindFirstValue(ClaimTypes.Email)!;
            return Results.Ok(new UserResponse(id, email));
        }).RequireAuthorization();
    }

    private static IResult? ValidateLogin(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Email"] = ["Email is required."]
            });
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Password"] = ["Password is required."]
            });
        }

        return null;
    }

    private static UserResponse ToResponse(User user) => new(user.Id, user.Email);
}
