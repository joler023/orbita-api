using Microsoft.Extensions.Logging;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Tenants;

namespace Orbita.Application.Ai;

/// <summary>
/// The queue half of ORB-C02's pipeline: claim pending documents, drive their status, and
/// decide what a failure means. Building the chunks themselves is
/// <see cref="IDocumentChunkBuilder"/>'s job.
///
/// <para><b>Why it walks tenants.</b> The worker runs outside any request, so it has no
/// ambient tenant — and Row Level Security returns zero rows, not all rows, when none is
/// set. A single "find every pending document" query would therefore silently find
/// nothing against the real database while passing happily against an unfiltered one. So
/// the loop reads the tenant list (the one table that is not tenant-scoped), adopts each
/// tenant's scope, and asks for its pending work. That is O(tenants) queries per pass,
/// which is fine at this scale; when it stops being fine the answer is the
/// <c>outbox_events</c> pattern orbita-schema.dbml already describes, not an RLS
/// bypass.</para>
///
/// <para><b>Why two kinds of failure.</b> A document that cannot be read is the
/// <em>document's</em> problem — recorded on the row in Spanish, and the loop moves on. A
/// model provider that is down is <em>not</em>, so the document goes back to
/// <c>Pending</c> instead: telling someone their catalogue is corrupt because the
/// embedding provider was restarting would send them off to re-upload a perfectly good
/// file.</para>
/// </summary>
public sealed class KnowledgeIndexer(
    ITenantRepository tenantRepository,
    IKnowledgeDocumentRepository documentRepository,
    IDocumentChunkBuilder chunkBuilder,
    ITenantContextSetter tenantContextSetter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<KnowledgeIndexer> logger) : IKnowledgeIndexer
{
    /// <summary>Per tenant, per pass — so one bulk upload cannot starve every other tenant.</summary>
    public const int MaxDocumentsPerTenantPerPass = 5;

    public async Task<int> IndexPendingAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await tenantRepository.ListActiveIdsAsync(cancellationToken);
        var indexed = 0;

        foreach (var tenantId in tenantIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            indexed += await IndexTenantAsync(tenantId, cancellationToken);
        }

        return indexed;
    }

    private async Task<int> IndexTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        tenantContextSetter.SetTenant(tenantId);

        var pending = await unitOfWork.QueryInTenantScopeAsync(
            ct => documentRepository.ListPendingAsync(tenantId, MaxDocumentsPerTenantPerPass, ct),
            cancellationToken);

        var indexed = 0;

        foreach (var document in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await IndexDocumentAsync(document, cancellationToken))
            {
                indexed++;
            }
        }

        return indexed;
    }

    private async Task<bool> IndexDocumentAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        document.MarkProcessing();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var chunkCount = await chunkBuilder.BuildAsync(document, cancellationToken);
            document.MarkIndexed(chunkCount, timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Indexed knowledge document {DocumentId} into {ChunkCount} chunks.", document.Id, chunkCount);
            return true;
        }
        catch (LlmProviderException failure)
        {
            logger.LogWarning(
                failure,
                "Embedding provider unavailable while indexing {DocumentId}; leaving it pending for a later pass.",
                document.Id);

            document.MarkForReindex();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or NotSupportedException or ArgumentException)
        {
            document.MarkFailed(ReasonFor(exception));
            await unitOfWork.SaveChangesAsync(cancellationToken);

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
