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

    /// <summary>
    /// The chunks closest to <paramref name="queryEmbedding"/> by cosine similarity
    /// (ORB-C03), newest-best first.
    ///
    /// Filtered by agent as well as tenant: two agents in the same organization can be
    /// given different documents on purpose — a support assistant and a sales assistant
    /// should not answer from each other's material.
    /// </summary>
    /// <param name="queryEmbedding">
    /// Must have <see cref="KnowledgeChunk.EmbeddingDimensions"/> components, produced by
    /// the same model the chunks were embedded with. Vectors from different models are
    /// not comparable, so a mismatch is meaningless rather than merely inaccurate.
    /// </param>
    Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        Guid tenantId,
        Guid agentId,
        IReadOnlyList<float> queryEmbedding,
        int limit,
        CancellationToken cancellationToken);
}
