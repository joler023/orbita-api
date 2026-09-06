using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Identity;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A07: invite, resend, revoke, and accept team invitations.</summary>
[ApiController]
public sealed class TeamInvitationsController(
    ITeamInvitationService invitationService,
    IAuthenticationService authenticationService,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("api/tenants/{tenantId:guid}/invitations")]
    [Authorize]
    [ProducesResponseType(typeof(TeamInvitationSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TeamInvitationSummary>> Invite(
        Guid tenantId,
        [FromBody] InviteTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        var summary = await invitationService.InviteAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, summary);
    }

    [HttpPost("api/tenants/{tenantId:guid}/invitations/{membershipId:guid}/resend")]
    [Authorize]
    [ProducesResponseType(typeof(TeamInvitationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamInvitationSummary>> Resend(
        Guid tenantId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var summary = await invitationService.ResendAsync(tenantId, User.GetUserId(), membershipId, cancellationToken);
        return Ok(summary);
    }

    [HttpDelete("api/tenants/{tenantId:guid}/invitations/{membershipId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        await invitationService.RevokeAsync(tenantId, User.GetUserId(), membershipId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Anonymous — the invited person may not have a session (or even an account)
    /// yet. The token itself is what proves they were the one emailed. On success,
    /// this also logs them in with the password they just set or confirmed, exactly
    /// like AuthController.Login does, so accepting an invite drops them straight
    /// into the product.
    /// </summary>
    [HttpPost("api/invitations/accept")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Accept(
        [FromBody] AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var accepted = await invitationService.AcceptAsync(request, cancellationToken);
        var session = await authenticationService.LoginAsync(new LoginRequest(accepted.Email, request.Password), cancellationToken);
        AuthCookies.Append(Response, session, secure: !environment.IsDevelopment());
        return Ok(new CurrentUserResponse(accepted.UserId, accepted.Email, accepted.FullName));
    }
}
