using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// One document waiting to be indexed.
///
/// <para><b>Why this table exists.</b> The indexer runs outside any request, so it has no
/// ambient tenant — and Row Level Security returns <em>zero</em> rows, not all rows, when
/// none is set. So it cannot simply ask "which documents are pending?" across the
/// product. The first version walked every active tenant and asked each one in turn,
/// which cost one query per tenant on every pass: correct, but it gets worse purely
/// because the business grows, which is the wrong thing to have get worse.
///
/// This queue makes the worker's cost proportional to <em>pending work</em> instead of to
/// tenant count. It is the <c>outbox_events</c> pattern orbita-schema.dbml already
/// describes, applied to indexing.</para>
///
/// <para><b>Why it is not tenant-scoped.</b> Deliberately no RLS policy and no query
/// filter, for the same reason as <c>InvitationToken</c>, <c>PasswordResetToken</c> and
/// <c>Subscription</c>: it is read <em>before</em> the tenant is known — learning the
/// tenant is the whole point of reading it. It holds no content, only two ids and a
/// timestamp, and the worker uses <see cref="TenantId"/> to establish scope before
/// touching anything that does carry content. Every read of a document or a chunk still
/// goes through RLS normally.</para>
///
/// The primary key is the document's own id, so enqueueing the same document twice is
/// idempotent rather than producing duplicate work.
/// </summary>
public sealed class KnowledgeIndexingQueueEntry : Entity
{
    // The parameter is named `id`, not `documentId`, because EF Core binds constructor
    // parameters to mapped properties by name and the mapped property here is Entity.Id.
    private KnowledgeIndexingQueueEntry(Guid id, Guid tenantId, DateTimeOffset enqueuedAt)
        : base(id)
    {
        TenantId = tenantId;
        EnqueuedAt = enqueuedAt;
    }

    /// <summary>Which tenant's scope the worker must adopt before reading the document.</summary>
    public Guid TenantId { get; }

    /// <summary>Oldest first, so a large upload cannot starve a document queued after it.</summary>
    public DateTimeOffset EnqueuedAt { get; }

    /// <summary>The queued document — the entry's <see cref="Entity.Id"/> is that document's id.</summary>
    public Guid DocumentId => Id;

    public static KnowledgeIndexingQueueEntry For(KnowledgeDocument document, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new KnowledgeIndexingQueueEntry(document.Id, document.TenantId, now);
    }
}
