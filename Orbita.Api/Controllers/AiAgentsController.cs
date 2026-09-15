using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-C10: the assistants a tenant runs, and how they are configured. Every action
/// requires <c>Permission.ManageAiAgents</c> (Owner or Admin).
///
/// The shape here is what screens 2.5 and 2.6 need, which is deliberately narrower than
/// the row behind it: no prompt, no model, no token budget, no temperature. See
/// <see cref="AiAgentDto"/>.
/// </summary>
[ApiController]
[Authorize]
public sealed class AiAgentsController(IAiAgentService aiAgents) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/ai-agents")]
    [ProducesResponseType(typeof(IReadOnlyList<AiAgentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AiAgentDto>>> List(Guid tenantId, CancellationToken cancellationToken)
        => Ok(await aiAgents.ListAsync(tenantId, User.GetUserId(), cancellationToken));

    [HttpGet("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}")]
    [ProducesResponseType(typeof(AiAgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiAgentDto>> Get(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => Ok(await aiAgents.GetAsync(tenantId, User.GetUserId(), agentId, cancellationToken));

    [HttpPost("api/tenants/{tenantId:guid}/ai-agents")]
    [ProducesResponseType(typeof(AiAgentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AiAgentDto>> Create(
        Guid tenantId,
        [FromBody] SaveAiAgentBody body,
        CancellationToken cancellationToken)
    {
        var agent = await aiAgents.CreateAsync(tenantId, User.GetUserId(), body.ToRequest(), cancellationToken);

        return CreatedAtAction(nameof(Get), new { tenantId, agentId = agent.Id }, agent);
    }

    /// <summary>
    /// "Guardar". Parks the changes as a draft — the assistant that is answering customers
    /// is untouched until <see cref="Publish"/>. The UX document is explicit that nobody
    /// should edit in place an agent that is live.
    /// </summary>
    [HttpPatch("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}")]
    [ProducesResponseType(typeof(AiAgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiAgentDto>> SaveDraft(
        Guid tenantId,
        Guid agentId,
        [FromBody] SaveAiAgentBody body,
        CancellationToken cancellationToken)
        => Ok(await aiAgents.SaveDraftAsync(tenantId, User.GetUserId(), agentId, body.ToRequest(), cancellationToken));

    /// <summary>
    /// "Publicar". Copies the pending draft onto the live assistant. Does <b>not</b> enable
    /// it — publishing a configuration and putting an assistant in front of customers are
    /// separate decisions.
    /// </summary>
    [HttpPost("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/publish")]
    [ProducesResponseType(typeof(AiAgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AiAgentDto>> Publish(
        Guid tenantId,
        Guid agentId,
        CancellationToken cancellationToken)
        => Ok(await aiAgents.PublishAsync(tenantId, User.GetUserId(), agentId, cancellationToken));

    /// <summary>Throws away the pending draft. The live assistant is untouched.</summary>
    [HttpDelete("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/draft")]
    [ProducesResponseType(typeof(AiAgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiAgentDto>> DiscardDraft(
        Guid tenantId,
        Guid agentId,
        CancellationToken cancellationToken)
        => Ok(await aiAgents.DiscardDraftAsync(tenantId, User.GetUserId(), agentId, cancellationToken));

    /// <summary>
    /// A subresource rather than a field on the full update: screen 2.5 flips this straight
    /// from the list, where the rest of the agent is not loaded.
    /// </summary>
    [HttpPatch("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/enabled")]
    [ProducesResponseType(typeof(AiAgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiAgentDto>> SetEnabled(
        Guid tenantId,
        Guid agentId,
        [FromBody] SetAgentEnabledRequest request,
        CancellationToken cancellationToken)
        => Ok(await aiAgents.SetEnabledAsync(tenantId, User.GetUserId(), agentId, request.IsEnabled, cancellationToken));

    [HttpDelete("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
    {
        await aiAgents.DeleteAsync(tenantId, User.GetUserId(), agentId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// The catalog of actions an assistant can be given. Not tenant-scoped and not
    /// authorized beyond being signed in: it is product information, identical for
    /// everyone, and the frontend needs it to render screen 2.6's checkboxes.
    /// </summary>
    [HttpGet("api/ai-tools")]
    [ProducesResponseType(typeof(IReadOnlyList<AiTool>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AiTool>> ListTools() => Ok(aiAgents.ListTools());
}

/// <param name="Tools">
/// The complete set of enabled tools, not a delta. Omitting it means "none"; the screen
/// always sends the full checkbox state.
/// </param>
public sealed record SaveAiAgentBody(
    [Required, MaxLength(AiAgent.NameMaxLength)] string Name,
    [Required, MaxLength(AiAgent.PersonalityMaxLength)] string Personality,
    [Required, MaxLength(AiAgent.InstructionsMaxLength)] string Instructions,
    [Required] AgentStyleDto Style,
    IReadOnlyList<string>? Tools = null)
{
    public SaveAiAgentRequest ToRequest() => new(Name, Personality, Instructions, Style, Tools ?? []);
}

public sealed record SetAgentEnabledRequest(bool IsEnabled);
