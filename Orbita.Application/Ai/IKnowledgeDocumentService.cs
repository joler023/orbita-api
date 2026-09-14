using Orbita.Application.Common;

namespace Orbita.Application.Ai;

/// <summary>
/// Managing the documents an agent answers from (ORB-C02). Every method requires
/// <c>Permission.ManageAiAgents</c>.
///
/// Uploading only *registers* a document — extraction, chunking and embedding happen in
/// the background, which is why callers get back a document in
/// <c>KnowledgeDocStatus.Pending</c> and poll for the rest.
/// </summary>
public interface IKnowledgeDocumentService
{
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    /// <exception cref="UnsupportedDocumentTypeException">Not a PDF, DOCX, TXT or Markdown file.</exception>
    /// <exception cref="DocumentTooLargeException">Over the upload size limit.</exception>
    Task<KnowledgeDocumentDto> UploadAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        UploadKnowledgeDocumentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Text pasted straight into the form — no file, indexed the same way.</summary>
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    Task<KnowledgeDocumentDto> AddPastedTextAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        string title,
        string text,
        CancellationToken cancellationToken);

    Task<CursorPage<KnowledgeDocumentDto>> ListAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);

    /// <exception cref="KnowledgeDocumentNotFoundException">No such document in this tenant.</exception>
    Task<KnowledgeDocumentDto> ReindexAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid documentId,
        CancellationToken cancellationToken);

    /// <exception cref="KnowledgeDocumentNotFoundException">No such document in this tenant.</exception>
    Task DeleteAsync(Guid tenantId, Guid callerUserId, Guid documentId, CancellationToken cancellationToken);
}

/// <param name="FileName">Used for the title and to pick a text extractor by extension.</param>
/// <param name="Content">The caller keeps ownership of the stream; the service copies what it needs.</param>
public sealed record UploadKnowledgeDocumentRequest(string FileName, Stream Content, long SizeBytes);
