using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;

namespace Orbita.Api.Controllers;

/// <summary>ORB-C12: the assistant's answer cache — its threshold and how often it pays off.</summary>
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
    /// Sending <c>null</c> for <c>threshold</c> turns the cache off. Not part of the draft:
    /// like the guardrails, it changes what customers get the moment it is saved.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(SemanticCacheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SemanticCacheDto>> SetThreshold(
        Guid tenantId,
        Guid agentId,
        [FromBody] SetSemanticCacheBody body,
        CancellationToken cancellationToken)
        => Ok(await semanticCache.SetThresholdAsync(tenantId, User.GetUserId(), agentId, body.Threshold, cancellationToken));
}

public sealed record SetSemanticCacheBody(decimal? Threshold);
