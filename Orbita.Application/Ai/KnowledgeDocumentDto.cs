using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// A knowledge document as the dashboard sees it (ORB-C02, screen 2.7).
/// </summary>
/// <param name="Status">
/// Serialized as its name (<c>"Processing"</c>), like every other enum on this API.
/// </param>
/// <param name="ChunkCount">
/// How many pieces the document was split into. Zero until indexing finishes, so it
/// doubles as the "how much is there" signal once it does.
/// </param>
/// <param name="FailureReason">
/// Written in Spanish and phrased as what to do about it — the UI shows it verbatim, so
/// it must never carry an exception type or a stack trace. Null unless
/// <paramref name="Status"/> is <c>Failed</c>.
/// </param>
public sealed record KnowledgeDocumentDto(
    Guid Id,
    Guid AgentId,
    string Title,
    KnowledgeDocSourceType SourceType,
    KnowledgeDocStatus Status,
    int ChunkCount,
    string? FailureReason,
    DateTimeOffset? IndexedAt,
    DateTimeOffset CreatedAt)
{
    public static KnowledgeDocumentDto From(KnowledgeDocument document)
        => new(
            document.Id,
            document.AgentId,
            document.Title,
            document.SourceType,
            document.Status,
            document.ChunkCount,
            document.FailureReason,
            document.IndexedAt,
            document.CreatedAt);
}
