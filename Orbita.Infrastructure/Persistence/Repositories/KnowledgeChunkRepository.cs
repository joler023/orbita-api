using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;
using Pgvector;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeChunkRepository(OrbitaDbContext dbContext) : IKnowledgeChunkRepository
{
    public void AddRange(IEnumerable<KnowledgeChunk> chunks) => dbContext.KnowledgeChunks.AddRange(chunks);

    /// <summary>
    /// A bulk delete rather than load-then-remove: a 50-page document can be hundreds of
    /// chunks, and none of them needs to pass through the change tracker to be dropped.
    /// </summary>
    public Task DeleteByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
        => dbContext.KnowledgeChunks
            .Where(c => c.TenantId == tenantId && c.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> CountByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
        => dbContext.KnowledgeChunks
            .CountAsync(c => c.TenantId == tenantId && c.DocumentId == documentId, cancellationToken);

    /// <summary>
    /// ORB-C03's cosine search.
    ///
    /// Written against <c>&lt;=&gt;</c>, pgvector's cosine-distance operator, because that
    /// is the operator the HNSW index is built for (<c>vector_cosine_ops</c>) — any other
    /// distance expression would parse fine and then quietly do a full scan, which is
    /// exactly the failure the 200 ms acceptance criterion is there to catch.
    ///
    /// The join to <c>knowledge_docs</c> both supplies the document title for citation and
    /// restricts results to one agent's material.
    ///
    /// Distance is converted to similarity here (<c>1 - distance</c>) so callers get the
    /// "higher is better" number every UI expects.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        Guid tenantId,
        Guid agentId,
        IReadOnlyList<float> queryEmbedding,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new Vector(queryEmbedding.ToArray());

        var rows = await dbContext.Database
            .SqlQuery<ChunkSearchRow>($"""
                SELECT  c.id            AS "ChunkId",
                        c.doc_id        AS "DocumentId",
                        d.title         AS "DocumentTitle",
                        c.chunk_index   AS "ChunkIndex",
                        c.content       AS "Content",
                        1 - (c.embedding <=> {query}) AS "Score"
                FROM    knowledge_chunks c
                JOIN    knowledge_docs   d ON d.id = c.doc_id
                WHERE   c.tenant_id = {tenantId}
                  AND   d.agent_id  = {agentId}
                ORDER BY c.embedding <=> {query}
                LIMIT   {limit}
                """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new KnowledgeSearchHit(
                row.ChunkId,
                row.DocumentId,
                row.DocumentTitle,
                row.ChunkIndex,
                row.Content,
                row.Score))
            .ToList();
    }

    /// <summary>
    /// Shape of the projection above. A dedicated type rather than an anonymous one
    /// because <c>SqlQuery</c> needs something it can map column names onto.
    /// </summary>
    private sealed record ChunkSearchRow(
        Guid ChunkId,
        Guid DocumentId,
        string DocumentTitle,
        int ChunkIndex,
        string Content,
        double Score);
}
