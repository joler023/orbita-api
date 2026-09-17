using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C10: creating and configuring the assistants a tenant runs. Every method requires
/// <c>Permission.ManageAiAgents</c> (Owner or Admin) — an assistant's instructions decide
/// what customers are told, so it is not something a Viewer or an Agent changes.
///
/// <b>Saving is not publishing.</b> <see cref="SaveDraftAsync"/> parks changes;
/// <see cref="PublishAsync"/> makes them live. The UX document is explicit that nobody
/// should edit in place an assistant that is answering customers.
/// </summary>
public interface IAiAgentService
{
    Task<IReadOnlyList<AiAgentDto>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    Task<AiAgentDto> GetAsync(Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an assistant, live. There is nothing to protect yet — a brand new assistant
    /// is disabled and has no customers — so creation does not go through a draft.
    /// </summary>
    Task<AiAgentDto> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        SaveAiAgentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// "Guardar": parks the changes without touching the assistant that is answering
    /// customers. Saving again replaces the pending draft rather than stacking another.
    /// </summary>
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    Task<AiAgentDto> SaveDraftAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        SaveAiAgentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// "Publicar": copies the pending draft onto the live assistant and clears it. Does not
    /// enable the assistant — see <see cref="SetEnabledAsync"/>.
    /// </summary>
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    /// <exception cref="NothingToPublishException">There is no pending draft.</exception>
    Task<AiAgentDto> PublishAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        CancellationToken cancellationToken);

    /// <summary>Throws away the pending draft, leaving the live assistant untouched.</summary>
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    Task<AiAgentDto> DiscardDraftAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Turning an assistant on or off, on its own. Screen 2.5 toggles it straight from the
    /// list, so making it part of the full update would force the UI to hold a whole agent
    /// just to flip a switch. Independent of publishing.
    /// </summary>
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    /// <summary>
    /// ORB-C06. Replaces the blocked topics and the out-of-scope reply together, and
    /// applies them to the live assistant immediately — no draft, no publish.
    /// </summary>
    /// <param name="handoffReply">
    /// ORB-C07's sentence, added after this endpoint already had a consumer. Null keeps
    /// whatever is stored, so a client written against the two-field body still saves —
    /// the frontend asked for exactly this, having spotted that a required field here
    /// would break its limits screen for a business owner rather than for us.
    /// </param>
    /// <exception cref="AiAgentNotFoundException"/>
    /// <exception cref="ArgumentException">Too many topics, a topic or a reply too long, or an empty reply.</exception>
    Task<AiAgentDto> SetGuardrailsAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        IReadOnlyList<string> blockedTopics,
        string outOfScopeReply,
        string? handoffReply,
        CancellationToken cancellationToken);

    Task<AiAgentDto> SetEnabledAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        bool isEnabled,
        CancellationToken cancellationToken);

    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    /// <exception cref="CannotDeleteLastAgentException">It is the tenant's only assistant.</exception>
    Task DeleteAsync(Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken);

    /// <summary>The catalog of actions an assistant can be given. Product-level, identical for every tenant.</summary>
    IReadOnlyList<AiTool> ListTools();
}

/// <summary>
/// What screen 2.6 collects. The same shape for create and for saving a draft, because it
/// is the same form in both cases.
/// </summary>
/// <param name="Tools">The complete set of enabled tools, not a delta — the screen edits checkboxes.</param>
public sealed record SaveAiAgentRequest(
    string Name,
    string Personality,
    string Instructions,
    AgentStyleDto Style,
    IReadOnlyList<string> Tools);
