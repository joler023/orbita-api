using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Identity;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-A05: lets a business owner create their account — tenant, owner user and
/// owner membership — with no salesperson involved (Guía de pantallas 1.1 · Registro).
/// </summary>
[ApiController]
[Route("api/organizations")]
public sealed class OrganizationsController(IOrganizationRegistrationService registrationService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(RegisterOrganizationResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterOrganizationResult>> Register(
        [FromBody] RegisterOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.RegisterAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(TenantsController.GetById),
            "Tenants",
            new { id = result.TenantId },
            result);
    }
}
