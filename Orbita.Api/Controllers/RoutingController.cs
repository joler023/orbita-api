using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Api.Controllers;

/// <summary>ORB-C08: the router's ordered rules and each assistant's business hours.</summary>
[ApiController]
[Authorize]
public sealed class RoutingController(IRoutingService routing) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/routing/rules")]
    [ProducesResponseType(typeof(IReadOnlyList<RoutingRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<RoutingRuleDto>>> ListRules(Guid tenantId, CancellationToken cancellationToken)
        => Ok(await routing.ListRulesAsync(tenantId, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Replaces the whole list; the order of the array is the evaluation order. A reorder
    /// is a PUT with the same rules in a different order.
    /// </summary>
    [HttpPut("api/tenants/{tenantId:guid}/routing/rules")]
    [ProducesResponseType(typeof(IReadOnlyList<RoutingRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RoutingRuleDto>>> ReplaceRules(
        Guid tenantId,
        [FromBody] ReplaceRoutingRulesBody body,
        CancellationToken cancellationToken)
        => Ok(await routing.ReplaceRulesAsync(tenantId, User.GetUserId(), body.Rules, cancellationToken));

    /// <summary>Sending <c>null</c> for <c>businessHours</c> clears them: the assistant is always on.</summary>
    [HttpPut("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/business-hours")]
    [ProducesResponseType(typeof(AiAgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiAgentDto>> SetBusinessHours(
        Guid tenantId,
        Guid agentId,
        [FromBody] SetBusinessHoursBody body,
        CancellationToken cancellationToken)
        => Ok(await routing.SetBusinessHoursAsync(tenantId, User.GetUserId(), agentId, body.BusinessHours, cancellationToken));
}

public sealed record ReplaceRoutingRulesBody([Required, MaxLength(RoutingRule.MaxRulesPerTenant)] IReadOnlyList<RoutingRuleRequest> Rules);

public sealed record SetBusinessHoursBody(BusinessHoursRequest? BusinessHours);
