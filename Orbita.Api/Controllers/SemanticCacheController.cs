using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Api.Controllers;

/// <summary>ORB-C12: the assistant's answer cache — how freely it reuses, and how often that pays off.</summary>
[ApiController]
[Authorize]
[Route("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/semantic-cache")]
public sealed class SemanticCacheController(ISemanticCacheService semanticCache) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(SemanticCacheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SemanticCacheDto>> Get(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => Ok(await semanticCache.GetAsync(tenantId, User.GetUserId(), agentId, cancellationToken));

    /// <summary>
    /// Sending <c>"Off"</c> turns the cache off. Not part of the draft: like the guardrails,
    /// it changes what customers get the moment it is saved.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(SemanticCacheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SemanticCacheDto>> SetLevel(
        Guid tenantId,
        Guid agentId,
        [FromBody] SetSemanticCacheBody body,
        CancellationToken cancellationToken)
        => Ok(await semanticCache.SetLevelAsync(tenantId, User.GetUserId(), agentId, body.Level, cancellationToken));
}

/// <param name="Level">One of <c>Off</c>, <c>Conservative</c>, <c>Balanced</c>, <c>Aggressive</c>.</param>
public sealed record SetSemanticCacheBody(SemanticCacheLevel Level);
