using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// A source document an agent answers from — orbita-schema.dbml's
/// <c>knowledge_docs</c> (ORB-C02: "quiero subir mi catálogo y mis preguntas frecuentes
/// para que el agente responda con información real de mi negocio y no se invente
/// cosas").
///
/// The whole lifecycle is driven through the methods here rather than by setting
/// <see cref="Status"/> from outside, so an illegal transition (indexed → processing, or
/// a failure with no reason) cannot be expressed.
/// </summary>
public sealed class KnowledgeDocument : Entity
{
    private KnowledgeDocument(
        Guid id,
        Guid tenantId,
        Guid agentId,
        string title,
        KnowledgeDocSourceType sourceType,
        string? sourceRef,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        AgentId = agentId;
        Title = title;
        SourceType = sourceType;
        SourceRef = sourceRef;
        Status = KnowledgeDocStatus.Pending;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    /// <summary>Knowledge belongs to an agent, not to the tenant at large — two agents can know different things.</summary>
    public Guid AgentId { get; }

    public string Title { get; private set; }

    public KnowledgeDocSourceType SourceType { get; }

    /// <summary>
    /// Where the original bytes live: an object key in the file store for an upload,
    /// null for text pasted straight into the form. orbita-schema.dbml calls for an R2
    /// key here; the local-disk store is a documented stand-in until R2 is provisioned.
    /// </summary>
    public string? SourceRef { get; }

    public KnowledgeDocStatus Status { get; private set; }

    public int ChunkCount { get; private set; }

    /// <summary>
    /// Why indexing failed, phrased for the person who uploaded the file — the product
    /// rule is that errors say what to do, not what threw. Null unless
    /// <see cref="Status"/> is <see cref="KnowledgeDocStatus.Failed"/>.
    ///
    /// Not in orbita-schema.dbml: same class of addition as ORB-A06's lockout columns.
    /// Without it the UI can only say "falló" and leave the user with no next step.
    /// </summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset? IndexedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static KnowledgeDocument Create(
        Guid tenantId,
        Guid agentId,
        string title,
        KnowledgeDocSourceType sourceType,
        string? sourceRef,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new KnowledgeDocument(Guid.NewGuid(), tenantId, agentId, title.Trim(), sourceType, sourceRef, now);
    }

    /// <summary>Claimed by the indexer. Clears any previous failure so a retry starts clean.</summary>
    public void MarkProcessing()
    {
        Status = KnowledgeDocStatus.Processing;
        FailureReason = null;
        IndexedAt = null;
    }

    public void MarkIndexed(int chunkCount, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);

        Status = KnowledgeDocStatus.Indexed;
        ChunkCount = chunkCount;
        FailureReason = null;
        IndexedAt = now;
    }

    /// <param name="reason">Shown to the user as-is, so it must be written in their language.</param>
    public void MarkFailed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = KnowledgeDocStatus.Failed;
        FailureReason = reason;
        ChunkCount = 0;
        IndexedAt = null;
    }

    /// <summary>
    /// Queues the document to be indexed again from its original source (ORB-C02:
    /// "se puede reindexar y eliminar un documento"). Its existing chunks are deleted by
    /// the indexer before new ones are written, so a reindex never leaves a mix of both.
    /// </summary>
    public void MarkForReindex()
    {
        Status = KnowledgeDocStatus.Pending;
        ChunkCount = 0;
        FailureReason = null;
        IndexedAt = null;
    }
}
