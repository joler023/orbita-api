using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Media;

public sealed class MediaService(
    IConversationRepository conversations,
    IMessageRepository messages,
    IMediaUrlSigner urlSigner,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IMediaService
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    public async Task<UploadUrlDto> CreateUploadUrlAsync(Guid tenantId, Guid callerUserId, Guid conversationId, CreateUploadUrlRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        // conversations is RLS'd: outside a tenant-scoped transaction this finds nothing
        // and every upload answers 404. See CLAUDE.md, "Reads of RLS'd tables must run
        // inside a tenant scope".
        var conversation = await unitOfWork.QueryInTenantScopeAsync(
            ct => conversations.GetByIdAsync(conversationId, ct),
            cancellationToken)
            ?? throw new ConversationNotFoundException();

        if (!MediaMimeCatalog.IsAllowed(request.Mime))
        {
            throw new UnsupportedMediaTypeException();
        }

        if (request.SizeBytes > MediaMimeCatalog.MaxSizeBytesFor(request.Mime))
        {
            throw new MediaTooLargeException();
        }

        var key = MediaKeyBuilder.ForMessage(tenantId, conversation.Id, Guid.NewGuid(), request.Mime);
        var expiresAt = timeProvider.GetUtcNow() + LinkLifetime;
        var token = urlSigner.CreateToken("put", key, expiresAt);

        return new UploadUrlDto(key, $"/api/media/{token}", expiresAt);
    }

    public async Task<MediaUrlDto> GetMessageMediaUrlAsync(Guid tenantId, Guid callerUserId, Guid messageId, CancellationToken cancellationToken)
    {
        // No dedicated "view inbox" permission exists yet (that lands with ORB-B12) —
        // SendMessages is the closest stand-in until then.
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        var message = await unitOfWork.QueryInTenantScopeAsync(
            ct => messages.GetByIdAsync(messageId, ct),
            cancellationToken);
        if (message is null || message.TenantId != tenantId || message.MediaKey is null)
        {
            throw new MessageNotFoundException();
        }

        var expiresAt = timeProvider.GetUtcNow() + LinkLifetime;
        var token = urlSigner.CreateToken("get", message.MediaKey, expiresAt);

        return new MediaUrlDto($"/api/media/{token}", expiresAt);
    }
}
