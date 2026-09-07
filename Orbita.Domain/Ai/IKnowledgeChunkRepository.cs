namespace Orbita.Domain.Ai;

public interface IKnowledgeChunkRepository
{
    void AddRange(IEnumerable<KnowledgeChunk> chunks);

    /// <summary>
    /// Wipes a document's chunks before it is reindexed or deleted. Reindexing must never
    /// leave old and new chunks mixed, since the old ones would keep matching searches
    /// with content that no longer exists in the source.
    /// </summary>
    Task DeleteByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken);

    Task<int> CountByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken);
}
