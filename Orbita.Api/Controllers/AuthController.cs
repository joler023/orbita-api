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

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserIdResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrentUserIdResponse> Me() => Ok(new CurrentUserIdResponse(User.GetUserId()));

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

public sealed record CurrentUserIdResponse(Guid UserId);
