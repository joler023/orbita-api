using Microsoft.Extensions.Logging;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;

namespace Orbita.Application.Ai;

/// <summary>
/// The queue half of ORB-C02's pipeline: claim queued documents, drive their status, and
/// decide what a failure means. Building the chunks themselves is
/// <see cref="IDocumentChunkBuilder"/>'s job.
///
/// <para><b>Cost.</b> One query to read the queue per pass, then work proportional to the
/// number of pending documents — not to the number of tenants. See
/// <see cref="KnowledgeIndexingQueueEntry"/> for why the queue is a table of its own.</para>
///
/// <para><b>Why two kinds of failure.</b> A document that cannot be read is the
/// <em>document's</em> problem — recorded on the row in Spanish, dropped from the queue,
/// and the loop moves on. A model provider that is down is <em>not</em>, so the document
/// stays queued and pending: telling someone their catalogue is corrupt because the
/// embedding provider was rate-limiting would send them off to re-upload a perfectly good
/// file.</para>
/// </summary>
public sealed class KnowledgeIndexer(
    IKnowledgeIndexingQueue queue,
    IKnowledgeDocumentRepository documentRepository,
    IDocumentChunkBuilder chunkBuilder,
    ITenantContextSetter tenantContextSetter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<KnowledgeIndexer> logger) : IKnowledgeIndexer
{
    /// <summary>How many documents one pass will take on before yielding.</summary>
    public const int BatchSize = 5;

    public async Task<int> IndexPendingAsync(CancellationToken cancellationToken)
    {
        var batch = await queue.PeekAsync(BatchSize, cancellationToken);
        var indexed = 0;

        foreach (var entry in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await ProcessAsync(entry, cancellationToken))
            {
                indexed++;
            }
        }

        return indexed;
    }

    private async Task<bool> ProcessAsync(KnowledgeIndexingQueueEntry entry, CancellationToken cancellationToken)
    {
        // Adopt the queued document's tenant before reading anything RLS-protected.
        tenantContextSetter.SetTenant(entry.TenantId);

        var document = await unitOfWork.QueryInTenantScopeAsync(
            ct => documentRepository.GetByIdAsync(entry.TenantId, entry.DocumentId, ct),
            cancellationToken);

        // Deleted, or already handled by an earlier pass that crashed after indexing but
        // before dequeuing. Either way there is nothing to do.
        if (document is null || document.Status is not KnowledgeDocStatus.Pending)
        {
            await queue.RemoveAsync(entry.DocumentId, cancellationToken);
            return false;
        }

        document.MarkProcessing();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var chunkCount = await chunkBuilder.BuildAsync(document, cancellationToken);
            document.MarkIndexed(chunkCount, timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await queue.RemoveAsync(entry.DocumentId, cancellationToken);

            logger.LogInformation(
                "Indexed knowledge document {DocumentId} into {ChunkCount} chunks.", document.Id, chunkCount);
            return true;
        }
        catch (LlmProviderException failure)
        {
            // The provider, not the document: stays queued for a later pass.
            logger.LogWarning(
                failure,
                "Embedding provider unavailable while indexing {DocumentId}; leaving it queued.",
                document.Id);

            document.MarkForReindex();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or NotSupportedException or ArgumentException)
        {
            document.MarkFailed(ReasonFor(exception));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await queue.RemoveAsync(entry.DocumentId, cancellationToken);

            logger.LogWarning(exception, "Knowledge document {DocumentId} could not be indexed.", document.Id);
            return false;
        }
    }

    /// <summary>
    /// Shown to the user verbatim, so it stays in Spanish and says what to do about it —
    /// never a type name or a stack trace.
    /// </summary>
    private static string ReasonFor(Exception exception) => exception switch
    {
        InvalidDataException or NotSupportedException => exception.Message,
        FileNotFoundException => "No se encontró el archivo original. Vuelve a subirlo.",
        _ => "No pudimos procesar este documento. Revisa que no esté dañado y vuelve a subirlo.",
    };
}
