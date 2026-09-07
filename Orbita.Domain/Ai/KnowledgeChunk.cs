using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// One embedded slice of a document — orbita-schema.dbml's <c>knowledge_chunks</c>, and
/// what ORB-C03's cosine search actually matches against.
///
/// orbita-schema.dbml's note is explicit that RAG lives in the same Postgres via
/// pgvector rather than in a dedicated vector database: "Postgres rinde de sobra hasta
/// millones de chunks y evitar un sistema de datos más vale mucho más que el rendimiento
/// marginal."
///
/// A chunk is immutable: reindexing a document deletes its chunks and writes new ones
/// rather than editing them, because an edited chunk whose embedding was not recomputed
/// would silently return wrong matches forever.
/// </summary>
public sealed class KnowledgeChunk : Entity
{
    private KnowledgeChunk(
        Guid id,
        Guid tenantId,
        Guid documentId,
        int chunkIndex,
        string content,
        int? tokenCount,
        float[] embedding)
        : base(id)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        ChunkIndex = chunkIndex;
        Content = content;
        TokenCount = tokenCount;
        Embedding = embedding;
    }

    public Guid TenantId { get; }

    public Guid DocumentId { get; }

    /// <summary>Position within the document, so a search hit can say where it came from.</summary>
    public int ChunkIndex { get; }

    public string Content { get; }

    public int? TokenCount { get; }

    /// <summary>
    /// The vector. Its length must equal <see cref="EmbeddingDimensions"/> — the
    /// <c>vector(n)</c> the column was created with. A mismatch is rejected here rather
    /// than at the database, so the error names the real problem (the embedding model
    /// changed) instead of surfacing as a Postgres type error.
    /// </summary>
    public float[] Embedding { get; }

    /// <summary>
    /// The one dimension this deployment indexes at.
    ///
    /// 768 rather than orbita-schema.dbml's 1536 because it is the common denominator
    /// across the models actually available: <c>nomic-embed-text</c> on a local Ollama is
    /// 768, and OpenAI's <c>text-embedding-3-small</c> can be asked for 768 explicitly.
    /// Picking the value only OpenAI produces would make the local, free path
    /// impossible.
    ///
    /// Changing this is a migration, not a setting: every existing chunk has to be
    /// re-embedded, because vectors from different models are not comparable.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    public static KnowledgeChunk Create(
        Guid tenantId,
        Guid documentId,
        int chunkIndex,
        string content,
        int? tokenCount,
        IReadOnlyList<float> embedding)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding.Count != EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"Embedding has {embedding.Count} dimensions but knowledge_chunks stores {EmbeddingDimensions}. "
                    + "The configured embedding model does not match the one this index was built with; "
                    + "changing models requires a migration and a full reindex.",
                nameof(embedding));
        }

        return new KnowledgeChunk(Guid.NewGuid(), tenantId, documentId, chunkIndex, content, tokenCount, [.. embedding]);
    }
}
