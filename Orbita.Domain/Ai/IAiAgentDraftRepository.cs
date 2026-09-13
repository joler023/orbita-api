namespace Orbita.Domain.Ai;

public interface IAiAgentDraftRepository
{
    void Add(AiAgentDraft draft);

    void Remove(AiAgentDraft draft);

    Task<AiAgentDraft?> GetByAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);

    /// <summary>
    /// The drafts pending for a set of agents, so the list screen can show the
    /// "unpublished changes" badge without one query per row.
    /// </summary>
    Task<IReadOnlyList<AiAgentDraft>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
