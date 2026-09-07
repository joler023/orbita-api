using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C03: finds the passages of an agent's knowledge that are closest in meaning to a
/// question, so ORB-C04 can ground its answer in them instead of inventing one.
/// </summary>
public interface IKnowledgeSearchService
{
    /// <param name="limit">
    /// How many passages to return. Kept small by default: everything returned here ends
    /// up in a prompt, and padding a prompt with weak matches makes answers worse, not
    /// better.
    /// </param>
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    /// <exception cref="LlmProviderException">The query could not be embedded.</exception>
    Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        string query,
        int limit,
        CancellationToken cancellationToken);
}
