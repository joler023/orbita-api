using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Identity;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A06: login, refresh-token rotation, logout. ORB-A10: password reset.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthenticationService authenticationService,
    ICurrentUserService currentUserService,
    IPasswordResetService passwordResetService,
    IWebHostEnvironment environment) : ControllerBase
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

    /// <summary>
    /// Who is signed in, and which organizations they can act as (ORB-A16).
    ///
    /// The organizations are the point. The access token carries no tenant claim and the
    /// tenant is a route parameter everywhere else, so without this a client that has just
    /// signed in — on a device it has never used — has nothing to tell it where to go.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUser>> Me(CancellationToken cancellationToken)
        => Ok(await currentUserService.GetAsync(User.GetUserId(), cancellationToken));

    /// <summary>
    /// Always responds 202 regardless of whether the email belongs to an account
    /// (ORB-A10) — the response must not be a way to enumerate registered accounts.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword([FromBody] RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        await passwordResetService.RequestAsync(request.Email, cancellationToken);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await passwordResetService.ResetAsync(request, cancellationToken);
        return NoContent();
    }

    private static CurrentUserResponse ToCurrentUser(AuthenticationResult result)
        => new(result.UserId, result.Email, result.FullName);
}

public sealed record CurrentUserResponse(Guid UserId, string Email, string FullName);
