using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C10: creating and configuring the assistants a tenant runs. Every method requires
/// <c>Permission.ManageAiAgents</c> (Owner or Admin) — an assistant's instructions decide
/// what customers are told, so it is not something a Viewer or an Agent changes.
/// </summary>
public interface IAiAgentService
{
    Task<IReadOnlyList<AiAgentDto>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    Task<AiAgentDto> GetAsync(Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken);

    Task<AiAgentDto> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        SaveAiAgentRequest request,
        CancellationToken cancellationToken);

    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    Task<AiAgentDto> UpdateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        SaveAiAgentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Turning an assistant on or off, on its own. Screen 2.5 toggles it straight from the
    /// list, so making it part of the full update would force the UI to hold a whole agent
    /// just to flip a switch.
    /// </summary>
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
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
/// What screen 2.6 collects. The same shape for create and update, because the screen is
/// the same form in both cases.
/// </summary>
/// <param name="Tools">
/// The complete set of enabled tools, not a delta — the screen edits them as checkboxes.
/// </param>
public sealed record SaveAiAgentRequest(
    string Name,
    string Personality,
    string Instructions,
    AgentStyleDto Style,
    IReadOnlyList<string> Tools);
