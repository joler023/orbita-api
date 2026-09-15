using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-C03: searching an agent's knowledge by meaning rather than by keyword.
///
/// Separate from <see cref="KnowledgeDocumentsController"/> on purpose — administering
/// documents and querying them change for different reasons, the same way ORB-A07's
/// invitations and ORB-A08's members are separate controllers.
/// </summary>
[ApiController]
[Authorize]
public sealed class KnowledgeSearchController(IKnowledgeSearchService knowledgeSearch) : ControllerBase
{
    /// <summary>
    /// A POST rather than a GET because the question is free text that can be long, and a
    /// query string would put customer wording into every access log along the way.
    /// </summary>
    [HttpPost("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/knowledge/search")]
    [ProducesResponseType(typeof(IReadOnlyList<KnowledgeSearchHit>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<KnowledgeSearchHit>>> Search(
        Guid tenantId,
        Guid agentId,
        [FromBody] SearchKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        var hits = await knowledgeSearch.SearchAsync(
            tenantId, User.GetUserId(), agentId, request.Query, request.Limit, cancellationToken);

        return Ok(hits);
    }
}

/// <param name="Query">The question, in the customer's own words.</param>
/// <param name="Limit">
/// How many passages to return. Zero or below means the service's default — small on
/// purpose, since everything returned here ends up in a prompt.
/// </param>
public sealed record SearchKnowledgeRequest(
    [Required, MaxLength(2_000)] string Query,
    int Limit = 0);
