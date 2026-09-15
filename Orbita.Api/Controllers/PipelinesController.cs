using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Crm;

namespace Orbita.Api.Controllers;

/// <summary>ORB-D04: pipelines and stages for a tenant.</summary>
[ApiController]
[Authorize]
public sealed class PipelinesController(IPipelineService pipelineService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/pipelines")]
    [ProducesResponseType(typeof(IReadOnlyList<PipelineSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PipelineSummary>>> List(Guid tenantId, CancellationToken cancellationToken)
    {
        return Ok(await pipelineService.ListAsync(tenantId, User.GetUserId(), cancellationToken));
    }

    [HttpGet("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}")]
    [ProducesResponseType(typeof(PipelineSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PipelineSummary>> Get(Guid tenantId, Guid pipelineId, CancellationToken cancellationToken)
    {
        return Ok(await pipelineService.GetAsync(tenantId, User.GetUserId(), pipelineId, cancellationToken));
    }

    [HttpPost("api/tenants/{tenantId:guid}/pipelines")]
    [ProducesResponseType(typeof(PipelineSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PipelineSummary>> Create(
        Guid tenantId,
        [FromBody] CreatePipelineRequest request,
        CancellationToken cancellationToken)
    {
        var pipeline = await pipelineService.CreateAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return Created($"/api/tenants/{tenantId}/pipelines/{pipeline.Id}", pipeline);
    }

    [HttpPatch("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}")]
    [ProducesResponseType(typeof(PipelineSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PipelineSummary>> Update(
        Guid tenantId,
        Guid pipelineId,
        [FromBody] UpdatePipelineRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await pipelineService.UpdateAsync(tenantId, User.GetUserId(), pipelineId, request, cancellationToken));
    }

    [HttpDelete("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid tenantId, Guid pipelineId, CancellationToken cancellationToken)
    {
        await pipelineService.DeleteAsync(tenantId, User.GetUserId(), pipelineId, cancellationToken);
        return NoContent();
    }

    [HttpPost("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}/stages")]
    [ProducesResponseType(typeof(PipelineSummary), StatusCodes.Status201Created)]
    public async Task<ActionResult<PipelineSummary>> CreateStage(
        Guid tenantId,
        Guid pipelineId,
        [FromBody] CreateStageRequest request,
        CancellationToken cancellationToken)
    {
        var pipeline = await pipelineService.CreateStageAsync(tenantId, User.GetUserId(), pipelineId, request, cancellationToken);
        return Created($"/api/tenants/{tenantId}/pipelines/{pipelineId}", pipeline);
    }

    [HttpPatch("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}/stages/{stageId:guid}")]
    [ProducesResponseType(typeof(PipelineSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<PipelineSummary>> UpdateStage(
        Guid tenantId,
        Guid pipelineId,
        Guid stageId,
        [FromBody] UpdateStageRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await pipelineService.UpdateStageAsync(tenantId, User.GetUserId(), pipelineId, stageId, request, cancellationToken));
    }

    [HttpPut("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}/stages/order")]
    [ProducesResponseType(typeof(PipelineSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<PipelineSummary>> ReorderStages(
        Guid tenantId,
        Guid pipelineId,
        [FromBody] ReorderStagesRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await pipelineService.ReorderStagesAsync(tenantId, User.GetUserId(), pipelineId, request, cancellationToken));
    }

    [HttpDelete("api/tenants/{tenantId:guid}/pipelines/{pipelineId:guid}/stages/{stageId:guid}")]
    [ProducesResponseType(typeof(PipelineSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PipelineSummary>> DeleteStage(
        Guid tenantId,
        Guid pipelineId,
        Guid stageId,
        [FromQuery] Guid? relocateToStageId,
        CancellationToken cancellationToken)
    {
        return Ok(await pipelineService.DeleteStageAsync(
            tenantId,
            User.GetUserId(),
            pipelineId,
            stageId,
            new DeleteStageRequest(relocateToStageId),
            cancellationToken));
    }
}
