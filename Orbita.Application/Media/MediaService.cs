using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Media;

public sealed class MediaService(
    IConversationRepository conversations,
    IMessageRepository messages,
    IMediaUrlSigner urlSigner,
    ITenantAuthorizationService authorizationService,
    TimeProvider timeProvider) : IMediaService
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    public async Task<UploadUrlDto> CreateUploadUrlAsync(Guid tenantId, Guid callerUserId, Guid conversationId, CreateUploadUrlRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken)
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

        var message = await messages.GetByIdAsync(messageId, cancellationToken);
        if (message is null || message.TenantId != tenantId || message.MediaKey is null)
        {
            throw new MessageNotFoundException();
        }

        var expiresAt = timeProvider.GetUtcNow() + LinkLifetime;
        var token = urlSigner.CreateToken("get", message.MediaKey, expiresAt);

        return new MediaUrlDto($"/api/media/{token}", expiresAt);
    }
}
