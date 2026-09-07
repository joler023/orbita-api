using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Identity;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A08: list, change the role of, and remove team members.</summary>
[ApiController]
[Authorize]
public sealed class TeamMembersController(ITeamMembersService teamMembersService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/members")]
    [ProducesResponseType(typeof(IReadOnlyList<TeamMemberSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<TeamMemberSummary>>> List(Guid tenantId, CancellationToken cancellationToken)
    {
        var members = await teamMembersService.ListAsync(tenantId, User.GetUserId(), cancellationToken);
        return Ok(members);
    }

    [HttpPatch("api/tenants/{tenantId:guid}/members/{membershipId:guid}/role")]
    [ProducesResponseType(typeof(TeamMemberSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TeamMemberSummary>> ChangeRole(
        Guid tenantId,
        Guid membershipId,
        [FromBody] ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var summary = await teamMembersService.ChangeRoleAsync(tenantId, User.GetUserId(), membershipId, request.Role, cancellationToken);
        return Ok(summary);
    }

    [HttpDelete("api/tenants/{tenantId:guid}/members/{membershipId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        await teamMembersService.RemoveAsync(tenantId, User.GetUserId(), membershipId, cancellationToken);
        return NoContent();
    }
}
