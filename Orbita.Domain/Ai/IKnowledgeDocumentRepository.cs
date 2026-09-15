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

}
