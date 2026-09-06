using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Identity;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A06: login, refresh-token rotation, logout.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthenticationService authenticationService, IWebHostEnvironment environment) : ControllerBase
{
    private const string AccessTokenCookie = "access_token";
    private const string RefreshTokenCookie = "refresh_token";

    [HttpPost("login")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request, cancellationToken);
        SetAuthCookies(result);
        return Ok(ToCurrentUser(result));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken))
        {
            return Unauthorized();
        }

        var result = await authenticationService.RefreshAsync(refreshToken, cancellationToken);
        SetAuthCookies(result);
        return Ok(ToCurrentUser(result));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken))
        {
            await authenticationService.LogoutAsync(refreshToken, cancellationToken);
        }

        Response.Cookies.Delete(AccessTokenCookie);
        Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserIdResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrentUserIdResponse> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Ok(new CurrentUserIdResponse(Guid.Parse(userId!)));
    }

    private void SetAuthCookies(AuthenticationResult result)
    {
        // Secure requires HTTPS, which local development and the test server don't
        // have — everywhere else (Production, Staging), it's mandatory.
        var secure = !environment.IsDevelopment();

        Response.Cookies.Append(AccessTokenCookie, result.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Expires = result.AccessTokenExpiresAt,
        });

        // Scoped to /api/auth: the refresh token only ever needs to travel to the
        // refresh and logout endpoints, not on every request.
        Response.Cookies.Append(RefreshTokenCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = result.RefreshTokenExpiresAt,
        });
    }

    private static CurrentUserResponse ToCurrentUser(AuthenticationResult result)
        => new(result.UserId, result.Email, result.FullName);
}

public sealed record CurrentUserResponse(Guid UserId, string Email, string FullName);

public sealed record CurrentUserIdResponse(Guid UserId);
