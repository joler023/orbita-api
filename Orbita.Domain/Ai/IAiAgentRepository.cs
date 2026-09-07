namespace Orbita.Domain.Ai;

public interface IAiAgentRepository
{
    void Add(AiAgent agent);

    /// <param name="tenantId">
    /// Passed explicitly and checked, even though the EF query filter and the RLS policy
    /// already scope by tenant — the same defense in depth the rest of the codebase uses.
    /// </param>
    Task<AiAgent?> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiAgent>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
