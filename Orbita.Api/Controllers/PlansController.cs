using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Billing;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A12: the public plan catalog.</summary>
[ApiController]
[Route("api/plans")]
[AllowAnonymous]
public sealed class PlansController(ISubscriptionService subscriptionService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> List(CancellationToken cancellationToken)
    {
        var plans = await subscriptionService.ListPlansAsync(cancellationToken);
        return Ok(plans);
    }
}
