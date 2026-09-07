using System.Globalization;
using System.Text;
using Orbita.Application.Common;
using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

public sealed class KnowledgeDocumentService(
    IAiAgentRepository agentRepository,
    IKnowledgeDocumentRepository documentRepository,
    IKnowledgeChunkRepository chunkRepository,
    IKnowledgeDocumentStorage storage,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IKnowledgeDocumentService
{
    /// <summary>
    /// 25 MB. Comfortably above the "documento de 50 páginas" ORB-C02 sets its
    /// performance target on, and low enough that one upload cannot exhaust the
    /// indexer.
    /// </summary>
    public const long MaxUploadBytes = 25 * 1024 * 1024;

    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    private static readonly string[] SupportedExtensions = [".pdf", ".docx", ".txt", ".md"];

    public async Task<KnowledgeDocumentDto> UploadAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        UploadKnowledgeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        if (request.SizeBytes > MaxUploadBytes)
        {
            throw new DocumentTooLargeException(request.SizeBytes, MaxUploadBytes);
        }

        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();

        // Rejected before anything is written: an unsupported file that reached storage
        // would sit there forever with no document row pointing at it.
        if (!SupportedExtensions.Contains(extension))
        {
            throw new UnsupportedDocumentTypeException(string.IsNullOrEmpty(extension) ? "sin extensión" : extension);
        }

        await RequireAgentAsync(tenantId, agentId, cancellationToken);

        var storageKey = await storage.SaveAsync(tenantId, request.FileName, request.Content, cancellationToken);

        var document = KnowledgeDocument.Create(
            tenantId,
            agentId,
            Path.GetFileNameWithoutExtension(request.FileName),
            KnowledgeDocSourceType.Upload,
            storageKey,
            timeProvider.GetUtcNow());

        documentRepository.Add(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeDocumentDto.From(document);
    }

    public async Task<KnowledgeDocumentDto> AddPastedTextAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        await RequireAgentAsync(tenantId, agentId, cancellationToken);

        // Pasted text still goes through the file store rather than a second column on
        // the row: the indexer then has exactly one way to read a document's source,
        // whatever it was created from.
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var storageKey = await storage.SaveAsync(tenantId, $"{title}.txt", content, cancellationToken);

        var document = KnowledgeDocument.Create(
            tenantId,
            agentId,
            title,
            KnowledgeDocSourceType.Manual,
            storageKey,
            timeProvider.GetUtcNow());

        documentRepository.Add(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeDocumentDto.From(document);
    }

    public async Task<CursorPage<KnowledgeDocumentDto>> ListAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var pageSize = Math.Clamp(limit <= 0 ? DefaultPageSize : limit, 1, MaxPageSize);
        var createdBefore = DecodeCursor(cursor);

        // A read with no save of its own, so it needs its own tenant-scoped transaction:
        // the one EnsurePermissionAsync opened has already committed, and SET LOCAL
        // app.tenant_id does not outlive it (see CLAUDE.md on ORB-A15).
        var documents = await unitOfWork.QueryInTenantScopeAsync(
            ct => documentRepository.ListByAgentAsync(tenantId, agentId, createdBefore, pageSize + 1, ct),
            cancellationToken);

        // One extra row is fetched purely to learn whether another page exists.
        var hasMore = documents.Count > pageSize;
        var page = hasMore ? documents.Take(pageSize).ToList() : documents;

        return new CursorPage<KnowledgeDocumentDto>(
            page.Select(KnowledgeDocumentDto.From).ToList(),
            hasMore ? EncodeCursor(page[^1].CreatedAt) : null);
    }

    public async Task<KnowledgeDocumentDto> ReindexAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var document = await RequireDocumentAsync(tenantId, documentId, cancellationToken);

        // The old chunks go now rather than when the indexer picks the document up: until
        // it does, searches would otherwise still match content the user has already been
        // told is being replaced.
        await chunkRepository.DeleteByDocumentAsync(tenantId, documentId, cancellationToken);
        document.MarkForReindex();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeDocumentDto.From(document);
    }

    public async Task DeleteAsync(Guid tenantId, Guid callerUserId, Guid documentId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var document = await RequireDocumentAsync(tenantId, documentId, cancellationToken);

        await chunkRepository.DeleteByDocumentAsync(tenantId, documentId, cancellationToken);
        documentRepository.Remove(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the row is gone, so a storage failure can never leave a document row
        // pointing at a file that no longer exists.
        if (document.SourceRef is { } storageKey)
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
        }
    }

    private async Task RequireAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await unitOfWork.QueryInTenantScopeAsync(
            ct => agentRepository.GetByIdAsync(tenantId, agentId, ct),
            cancellationToken);

        if (agent is null)
        {
            throw new AiAgentNotFoundException();
        }
    }

    private async Task<KnowledgeDocument> RequireDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await unitOfWork.QueryInTenantScopeAsync(
            ct => documentRepository.GetByIdAsync(tenantId, documentId, ct),
            cancellationToken);

        return document ?? throw new KnowledgeDocumentNotFoundException();
    }

    /// <summary>
    /// The cursor is just the last row's timestamp, round-tripped as a string. Opaque to
    /// callers by contract, so it can grow into a composite key later without breaking
    /// anyone.
    /// </summary>
    private static string EncodeCursor(DateTimeOffset createdAt)
        => createdAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset? DecodeCursor(string? cursor)
        => long.TryParse(cursor, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;
}
