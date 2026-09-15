using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Crm;

namespace Orbita.Api.Controllers;

/// <summary>ORB-D05: kanban cards on a tenant pipeline.</summary>
[ApiController]
[Authorize]
public sealed class OpportunitiesController(IOpportunityService opportunityService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}/board")]
    [ProducesResponseType(typeof(PipelineBoard), StatusCodes.Status200OK)]
    public async Task<ActionResult<PipelineBoard>> GetBoard(
        Guid tenantId,
        Guid pipelineId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] DateTimeOffset? createdFrom,
        [FromQuery] DateTimeOffset? createdTo,
        CancellationToken cancellationToken)
    {
        return Ok(await opportunityService.GetBoardAsync(
            tenantId,
            User.GetUserId(),
            pipelineId,
            new OpportunityBoardQuery(assignedToUserId, createdFrom, createdTo),
            cancellationToken));
    }

    [HttpPost("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}/opportunities")]
    [ProducesResponseType(typeof(OpportunitySummary), StatusCodes.Status201Created)]
    public async Task<ActionResult<OpportunitySummary>> Create(
        Guid tenantId,
        Guid pipelineId,
        [FromBody] CreateOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        var created = await opportunityService.CreateAsync(
            tenantId,
            User.GetUserId(),
            pipelineId,
            request,
            cancellationToken);
        return Created($"/api/tenants/{tenantId}/opportunities/{created.Id}", created);
    }

    [HttpPatch("api/tenants/{tenantId:guid}/opportunities/{opportunityId:guid}")]
    [ProducesResponseType(typeof(OpportunitySummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpportunitySummary>> Update(
        Guid tenantId,
        Guid opportunityId,
        [FromBody] UpdateOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await opportunityService.UpdateAsync(
            tenantId,
            User.GetUserId(),
            opportunityId,
            request,
            cancellationToken));
    }

    [HttpPost("api/tenants/{tenantId:guid}/opportunities/{opportunityId:guid}/move")]
    [ProducesResponseType(typeof(OpportunitySummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpportunitySummary>> Move(
        Guid tenantId,
        Guid opportunityId,
        [FromBody] MoveOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await opportunityService.MoveAsync(
            tenantId,
            User.GetUserId(),
            opportunityId,
            request,
            cancellationToken));
    }
}
