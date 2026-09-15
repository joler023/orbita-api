namespace Orbita.Domain.Ai;

/// <summary>
/// The work list the background indexer reads. See
/// <see cref="KnowledgeIndexingQueueEntry"/> for why it is a table of its own and why it
/// is deliberately not tenant-scoped.
/// </summary>
public interface IKnowledgeIndexingQueue
{
    /// <summary>
    /// Stages an entry. Not saved here — the caller's own <c>SaveChangesAsync</c> commits
    /// it alongside the document it points at, so the queue can never reference a document
    /// whose insert was rolled back.
    /// </summary>
    void Enqueue(KnowledgeIndexingQueueEntry entry);

    /// <summary>
    /// The next entries to work on, oldest first. One query regardless of how many
    /// tenants exist — no ambient tenant is needed or used.
    /// </summary>
    Task<IReadOnlyList<KnowledgeIndexingQueueEntry>> PeekAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Drops an entry once its document is finished with — indexed, failed, or gone.
    /// Commits immediately: it is not tenant-scoped, so there is nothing for it to ride
    /// along with, the same reasoning as <c>IRefreshTokenRepository.RevokeFamilyAsync</c>.
    ///
    /// An entry left behind by a crash is simply picked up again on a later pass; the
    /// indexer skips documents that are no longer pending, so a repeat is harmless.
    /// </summary>
    Task RemoveAsync(Guid documentId, CancellationToken cancellationToken);
}
