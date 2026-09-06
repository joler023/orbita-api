using System.Security.Claims;

namespace Orbita.Api.Identity;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id from the access token's `sub` claim — mapped to
    /// ClaimTypes.NameIdentifier by JwtSecurityTokenHandler's default inbound claim
    /// mapping, with a fallback to the raw "sub" name in case that mapping is ever
    /// disabled.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.Parse(value ?? throw new InvalidOperationException("No user id claim on the authenticated principal."));
    }
}
