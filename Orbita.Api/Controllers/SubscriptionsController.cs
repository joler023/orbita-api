using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Billing;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A12: a tenant's own subscription lifecycle.</summary>
[ApiController]
[Authorize]
public sealed class SubscriptionsController(ISubscriptionService subscriptionService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/subscription")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionDto>> Get(Guid tenantId, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionService.GetAsync(tenantId, User.GetUserId(), cancellationToken);
        return subscription is null ? NotFound() : Ok(subscription);
    }

    [HttpPost("api/tenants/{tenantId:guid}/subscription")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionDto>> Subscribe(
        Guid tenantId,
        [FromBody] SubscribeRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionService.SubscribeAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, subscription);
    }

    [HttpPatch("api/tenants/{tenantId:guid}/subscription")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionDto>> ChangePlan(
        Guid tenantId,
        [FromBody] ChangePlanRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionService.ChangePlanAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return Ok(subscription);
    }

    [HttpDelete("api/tenants/{tenantId:guid}/subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid tenantId, CancellationToken cancellationToken)
    {
        await subscriptionService.CancelAsync(tenantId, User.GetUserId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("api/tenants/{tenantId:guid}/subscription/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<InvoiceSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<InvoiceSummary>>> ListInvoices(Guid tenantId, CancellationToken cancellationToken)
    {
        var invoices = await subscriptionService.ListInvoicesAsync(tenantId, User.GetUserId(), cancellationToken);
        return Ok(invoices);
    }
}
