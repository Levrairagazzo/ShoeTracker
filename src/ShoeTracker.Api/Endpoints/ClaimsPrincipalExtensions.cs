using System.Security.Claims;

namespace ShoeTracker.Api.Endpoints;

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal has no NameIdentifier claim."));
}
