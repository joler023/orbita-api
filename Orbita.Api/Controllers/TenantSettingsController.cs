using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Tenants;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A11: tenant-wide settings an Owner/Admin can configure.</summary>
[ApiController]
[Authorize]
public sealed class TenantSettingsController(ITenantSettingsService tenantSettingsService) : ControllerBase
{
    /// <summary>
    /// Not enforced at login yet — see Tenant.RequireMfaForMembers for why. Setting
    /// this to true today only records the policy for when enforcement is built.
    /// </summary>
    [HttpPatch("api/tenants/{tenantId:guid}/settings/mfa-policy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateMfaPolicy(
        Guid tenantId,
        [FromBody] UpdateMfaPolicyRequest request,
        CancellationToken cancellationToken)
    {
        await tenantSettingsService.SetRequireMfaForMembersAsync(tenantId, User.GetUserId(), request.RequireMfaForMembers, cancellationToken);
        return NoContent();
    }
}
