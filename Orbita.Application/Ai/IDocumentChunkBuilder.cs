using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// Turns one document's source file into embedded chunks: read → extract → split →
/// embed → stage.
///
/// Split out of <see cref="IKnowledgeIndexer"/> because the two change for different
/// reasons. This changes when the *content* pipeline does — a new file format, a
/// different chunking strategy, another embedding model. The indexer changes when the
/// *queue* does — how work is claimed, how failures are retried. Keeping them together
/// meant one class with twelve dependencies and two jobs.
/// </summary>
public interface IDocumentChunkBuilder
{
    /// <summary>
    /// Stages the chunks for <paramref name="document"/>, replacing any it already had.
    /// Does not save — the caller commits, so the chunks, the document's new status and
    /// its <c>ai_runs</c> rows all land in one transaction.
    /// </summary>
    /// <returns>How many chunks were staged.</returns>
    /// <exception cref="LlmProviderException">The embedding provider is unavailable; the document is fine.</exception>
    /// <exception cref="InvalidDataException">The document has no readable text.</exception>
    /// <exception cref="NotSupportedException">No extractor handles this file type.</exception>
    /// <exception cref="FileNotFoundException">The original file is gone from storage.</exception>
    Task<int> BuildAsync(KnowledgeDocument document, CancellationToken cancellationToken);
}
