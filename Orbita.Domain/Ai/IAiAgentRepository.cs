namespace Orbita.Domain.Ai;

public interface IAiAgentRepository
{
    void Add(AiAgent agent);

    void Remove(AiAgent agent);

    /// <param name="tenantId">
    /// Passed explicitly and checked, even though the EF query filter and the RLS policy
    /// already scope by tenant — the same defense in depth the rest of the codebase uses.
    /// </param>
    Task<AiAgent?> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiAgent>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// The assistant that answers a conversation nobody has assigned one to yet
    /// (ORB-C04): the tenant's enabled one, oldest first when there is more than one.
    ///
    /// "Oldest" is a placeholder for a real decision, not a rule worth defending —
    /// choosing between several assistants by channel, schedule, tag or keyword is
    /// ORB-C08. What matters until then is that the choice is deterministic, so a tenant
    /// with two enabled assistants does not get a different one on every message.
    /// </summary>
    Task<AiAgent?> FindEnabledByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
