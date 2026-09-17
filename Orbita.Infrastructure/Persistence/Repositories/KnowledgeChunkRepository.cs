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
    /// <para><b>The shape of this query is what makes the index usable, and it was measured.</b>
    /// Written as a join to <c>knowledge_docs</c> for the agent filter, the planner never
    /// chose the HNSW index: it walked <c>doc_id</c> and sorted the survivors, which is
    /// correct and costs <b>650 ms with 100.000 chunks</b> — against ORB-C03's 200 ms
    /// criterion. Filtering by <c>doc_id IN (…)</c> instead, and joining for the title
    /// only after the limit, lets it scan the index: <b>1,4 ms</b> on the same corpus.
    /// The debt note on <c>AddKnowledgeChunkHnswIndex</c> (and local/notas-entorno.md)
    /// predicted this would bite "in the tens of thousands per tenant"; it does.</para>
    ///
    /// <para><c>hnsw.iterative_scan</c> is what keeps that fast plan <em>correct</em>. An
    /// approximate index scan returns its own top-N and the agent filter is applied after,
    /// so without it a search could come back with fewer than <c>limit</c> rows — or none,
    /// if the nearest vectors all belong to another agent's documents. <c>strict_order</c>
    /// keeps results ordered by real distance and makes the scan continue until enough
    /// rows pass the filter. Needs pgvector 0.8+ (0.8.6 in the test image, the local
    /// container and the managed database).</para>
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

        // Safe as SET LOCAL because the caller always runs this inside a tenant-scoped
        // transaction — it has to, or Row Level Security would answer with no rows at all.
        await dbContext.Database.ExecuteSqlRawAsync(
            "SET LOCAL hnsw.iterative_scan = strict_order", cancellationToken);

        var rows = await dbContext.Database
            .SqlQuery<ChunkSearchRow>($"""
                WITH hit AS (
                    SELECT  c.id          AS id,
                            c.doc_id      AS doc_id,
                            c.chunk_index AS chunk_index,
                            c.content     AS content,
                            1 - (c.embedding <=> {query}) AS score
                    FROM    knowledge_chunks c
                    WHERE   c.tenant_id = {tenantId}
                      AND   c.doc_id IN (
                                SELECT id FROM knowledge_docs
                                WHERE tenant_id = {tenantId} AND agent_id = {agentId})
                    ORDER BY c.embedding <=> {query}
                    LIMIT   {limit}
                )
                SELECT  hit.id          AS "ChunkId",
                        hit.doc_id      AS "DocumentId",
                        d.title         AS "DocumentTitle",
                        hit.chunk_index AS "ChunkIndex",
                        hit.content     AS "Content",
                        hit.score       AS "Score"
                FROM    hit
                JOIN    knowledge_docs d ON d.id = hit.doc_id
                ORDER BY hit.score DESC
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
