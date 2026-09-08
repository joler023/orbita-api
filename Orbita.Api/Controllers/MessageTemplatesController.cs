using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;

namespace Orbita.Api.Controllers;

/// <summary>ORB-B07: registering message templates locally and syncing their Meta approval status.</summary>
[ApiController]
[Authorize]
public sealed class MessageTemplatesController(IMessageTemplateService messageTemplateService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/templates")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageTemplateSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MessageTemplateSummary>>> List(
        Guid tenantId,
        [FromQuery] TemplateStatus? status,
        CancellationToken cancellationToken)
    {
        return Ok(await messageTemplateService.ListAsync(tenantId, User.GetUserId(), status, cancellationToken));
    }

    [HttpPost("api/tenants/{tenantId:guid}/templates")]
    [ProducesResponseType(typeof(MessageTemplateSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MessageTemplateSummary>> Create(
        Guid tenantId,
        [FromBody] CreateTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await messageTemplateService.CreateAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return Created($"/api/tenants/{tenantId}/templates/{template.Id}", template);
    }

    [HttpPost("api/tenants/{tenantId:guid}/templates/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(Guid tenantId, [FromQuery] Guid channelAccountId, CancellationToken cancellationToken)
    {
        var count = await messageTemplateService.SyncFromMetaAsync(tenantId, User.GetUserId(), channelAccountId, cancellationToken);
        return Ok(new { synced = count });
    }
}
