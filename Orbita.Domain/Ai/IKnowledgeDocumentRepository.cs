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
    /// A cheap summary of an assistant's documents — how many, and the latest moment any
    /// was created or finished indexing. ORB-C12 folds it into a cache entry's fingerprint,
    /// so uploading, reindexing or deleting a document retires every answer given before.
    /// </summary>
    Task<(int Count, DateTimeOffset? LatestChange)> SummarizeForAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);
}
