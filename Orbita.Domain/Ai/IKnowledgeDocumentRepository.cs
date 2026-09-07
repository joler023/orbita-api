namespace Orbita.Domain.Ai;

public interface IKnowledgeDocumentRepository
{
    void Add(KnowledgeDocument document);

    void Remove(KnowledgeDocument document);

    Task<KnowledgeDocument?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of an agent's documents, newest first, using keyset pagination — the
    /// shape agreed with the frontend for every list in the product, because offset
    /// paging degrades exactly where it matters most (a long list, scrolled far).
    /// </summary>
    /// <param name="createdBefore">Exclusive upper bound: the <c>CreatedAt</c> of the last row already shown.</param>
    Task<IReadOnlyList<KnowledgeDocument>> ListByAgentAsync(
        Guid tenantId,
        Guid agentId,
        DateTimeOffset? createdBefore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Documents waiting to be indexed in one tenant, oldest first.
    ///
    /// Scoped to a single tenant on purpose. The background indexer has no ambient tenant
    /// of its own, and Row Level Security returns <em>zero</em> rows — not all rows — when
    /// none is set, so a cross-tenant "find all pending work" query would silently do
    /// nothing against the real database. The indexer therefore walks the tenant list and
    /// asks each one in turn, rather than bypassing RLS (which CLAUDE.md rules out).
    /// </summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListPendingAsync(Guid tenantId, int limit, CancellationToken cancellationToken);
}
