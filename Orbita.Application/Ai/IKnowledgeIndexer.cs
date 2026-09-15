namespace Orbita.Application.Ai;

/// <summary>
/// Turns pending documents into searchable chunks: read the source, extract text, split
/// it, embed each piece, save. Driven by a background worker, never by a request —
/// ORB-C02 requires "los embeddings se generan en segundo plano y el estado del documento
/// es visible".
/// </summary>
public interface IKnowledgeIndexer
{
    /// <summary>
    /// Indexes whatever is pending, oldest first, across every tenant.
    ///
    /// Never throws for content problems: a document that cannot be read is marked
    /// <c>Failed</c> with a reason the user can act on, because one bad upload must not
    /// stop the queue behind it.
    /// </summary>
    /// <returns>How many documents ended up indexed.</returns>
    Task<int> IndexPendingAsync(CancellationToken cancellationToken);
}
