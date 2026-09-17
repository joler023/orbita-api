using Orbita.Application.Media;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;

namespace Orbita.Application.Ai;

/// <summary>
/// The content half of ORB-C02's pipeline. Knows nothing about queues, retries or tenant
/// scope — it is handed a document whose scope is already established and produces its
/// chunks.
/// </summary>
public sealed class DocumentChunkBuilder(
    IMediaStorage storage,
    IEnumerable<ITextExtractor> extractors,
    ITextChunker chunker,
    ILlmProvider llmProvider,
    IAiRunRecorder runRecorder,
    IKnowledgeChunkRepository chunkRepository,
    IUnitOfWork unitOfWork) : IDocumentChunkBuilder
{
    /// <summary>
    /// How many chunks go into one embedding call. OpenAI's endpoint accepts far more,
    /// but a batch is also the unit that is retried and the unit whose failure loses work,
    /// and 64 chunks is already ~75.000 characters — a whole document for most uploads,
    /// one round trip for a 50-page one.
    /// </summary>
    public const int EmbeddingBatchSize = 64;

    public async Task<int> BuildAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SourceRef is not { } storageKey)
        {
            throw new InvalidDataException("El documento no tiene un archivo asociado. Vuelve a subirlo.");
        }

        var text = await ExtractTextAsync(document.Title, storageKey, cancellationToken);
        var pieces = chunker.Split(text);

        if (pieces.Count == 0)
        {
            throw new InvalidDataException("El documento no contiene texto que se pueda leer.");
        }

        // Cleared first so a retry after a partial failure cannot leave two generations
        // of the same document matching the same search. Wrapped because a bulk delete
        // runs immediately rather than inside the indexer's SaveChangesAsync, so without
        // a tenant-scoped transaction RLS would match zero rows and silently delete
        // nothing.
        await unitOfWork.ExecuteInTenantScopeAsync(
            ct => chunkRepository.DeleteByDocumentAsync(document.TenantId, document.Id, ct),
            cancellationToken);

        var chunks = new List<KnowledgeChunk>(pieces.Count);

        // In batches, not one by one. Measured against the real provider, a 50-page
        // document embedded chunk by chunk took 712 s — ORB-C02 asks for under 120 —
        // because 62 sequential calls averaged 4,5 s each and one took 129 s on its own.
        // The tokens are the same either way; what disappears is the queueing.
        foreach (var batch in pieces.Chunk(EmbeddingBatchSize))
        {
            var embeddings = await llmProvider.EmbedBatchAsync(
                [.. batch.Select(piece => piece.Content)], document.TenantId, cancellationToken);

            // One run per call, because one call is what the provider bills — the same
            // rule ORB-C09 applies to a tool loop's rounds. Staged, not saved: the
            // indexer's SaveChangesAsync commits it alongside the chunks it paid for.
            runRecorder.Record(document.TenantId, document.AgentId, embeddings.Usage);

            for (var i = 0; i < batch.Length; i++)
            {
                chunks.Add(KnowledgeChunk.Create(
                    document.TenantId,
                    document.Id,
                    batch[i].Index,
                    batch[i].Content,
                    batch[i].EstimatedTokens,
                    embeddings.Vectors[i]));
            }
        }

        chunkRepository.AddRange(chunks);

        return chunks.Count;
    }

    private async Task<string> ExtractTextAsync(string title, string storageKey, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(storageKey).ToLowerInvariant();
        var extractor = extractors.FirstOrDefault(candidate => candidate.SupportedExtensions.Contains(extension))
            ?? throw new NotSupportedException($"No sabemos leer archivos «{extension}».");

        // ORB-B06's storage returns null for a missing object rather than throwing; the
        // indexer reports a missing original as a readable failure, so it becomes one here.
        await using var content = await storage.OpenReadAsync(storageKey, cancellationToken)
            ?? throw new FileNotFoundException("The stored document is missing.", storageKey);

        var text = await extractor.ExtractAsync(content, cancellationToken);

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidDataException($"No se pudo extraer texto de «{title}». ¿Es un PDF escaneado sin texto?")
            : text;
    }
}
