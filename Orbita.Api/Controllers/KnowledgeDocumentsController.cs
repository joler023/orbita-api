using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;
using Orbita.Application.Common;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-C02: the documents an AI agent answers from. Every action requires
/// <c>Permission.ManageAiAgents</c> (Owner or Admin).
///
/// Uploads return <c>202 Accepted</c>, not <c>201</c>: the row exists but the document is
/// not searchable until the background indexer has embedded it, so the honest answer is
/// "we took this", with the status to poll for.
/// </summary>
[ApiController]
[Authorize]
public sealed class KnowledgeDocumentsController(IKnowledgeDocumentService knowledgeDocuments) : ControllerBase
{
    [HttpPost("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/knowledge")]
    [RequestSizeLimit(KnowledgeDocumentService.MaxUploadBytes)]
    [ProducesResponseType(typeof(KnowledgeDocumentDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Upload(
        Guid tenantId,
        Guid agentId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Empty upload",
                Detail = "No se recibió ningún archivo.",
            });
        }

        await using var content = file.OpenReadStream();

        var document = await knowledgeDocuments.UploadAsync(
            tenantId,
            User.GetUserId(),
            agentId,
            new UploadKnowledgeDocumentRequest(file.FileName, content, file.Length),
            cancellationToken);

        return Accepted(document);
    }

    [HttpPost("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/knowledge/text")]
    [ProducesResponseType(typeof(KnowledgeDocumentDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeDocumentDto>> AddText(
        Guid tenantId,
        Guid agentId,
        [FromBody] AddKnowledgeTextRequest request,
        CancellationToken cancellationToken)
    {
        var document = await knowledgeDocuments.AddPastedTextAsync(
            tenantId,
            User.GetUserId(),
            agentId,
            request.Title,
            request.Text,
            cancellationToken);

        return Accepted(document);
    }

    [HttpGet("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/knowledge")]
    [ProducesResponseType(typeof(CursorPage<KnowledgeDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CursorPage<KnowledgeDocumentDto>>> List(
        Guid tenantId,
        Guid agentId,
        CancellationToken cancellationToken,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = KnowledgeDocumentService.DefaultPageSize)
    {
        var page = await knowledgeDocuments.ListAsync(tenantId, User.GetUserId(), agentId, cursor, limit, cancellationToken);
        return Ok(page);
    }

    [HttpPost("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/knowledge/{documentId:guid}/reindex")]
    [ProducesResponseType(typeof(KnowledgeDocumentDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Reindex(
        Guid tenantId,
        Guid agentId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var document = await knowledgeDocuments.ReindexAsync(tenantId, User.GetUserId(), documentId, cancellationToken);
        return Accepted(document);
    }

    [HttpDelete("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/knowledge/{documentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid tenantId,
        Guid agentId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await knowledgeDocuments.DeleteAsync(tenantId, User.GetUserId(), documentId, cancellationToken);
        return NoContent();
    }
}

/// <param name="Title">Shown as the document's name in the list.</param>
/// <param name="Text">Pasted content, indexed exactly like an uploaded file.</param>
public sealed record AddKnowledgeTextRequest(
    [property: Required, MaxLength(300)] string Title,
    [property: Required, MaxLength(500_000)] string Text);
