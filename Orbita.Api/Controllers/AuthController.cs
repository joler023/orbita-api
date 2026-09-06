using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Identity;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A06: login, refresh-token rotation, logout.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthenticationService authenticationService, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request, cancellationToken);
        AuthCookies.Append(Response, result, secure: !environment.IsDevelopment());
        return Ok(ToCurrentUser(result));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(AuthCookies.RefreshToken, out var refreshToken))
        {
            return Unauthorized();
        }

        var result = await authenticationService.RefreshAsync(refreshToken, cancellationToken);
        AuthCookies.Append(Response, result, secure: !environment.IsDevelopment());
        return Ok(ToCurrentUser(result));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(AuthCookies.RefreshToken, out var refreshToken))
        {
            await authenticationService.LogoutAsync(refreshToken, cancellationToken);
        }

        AuthCookies.Delete(Response);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserIdResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrentUserIdResponse> Me() => Ok(new CurrentUserIdResponse(User.GetUserId()));

    private static CurrentUserResponse ToCurrentUser(AuthenticationResult result)
        => new(result.UserId, result.Email, result.FullName);
}

public sealed record CurrentUserResponse(Guid UserId, string Email, string FullName);

public sealed record CurrentUserIdResponse(Guid UserId);
