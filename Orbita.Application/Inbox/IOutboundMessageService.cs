namespace Orbita.Application.Inbox;

public interface IOutboundMessageService
{
    /// <exception cref="ConversationNotFoundException"/>
    /// <exception cref="Channels.ChannelNotConnectedException"/>
    Task<MessageDto> SendTextAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendTextRequest request, CancellationToken cancellationToken);

    /// <exception cref="ConversationNotFoundException"/>
    /// <exception cref="Channels.ChannelNotConnectedException"/>
    /// <exception cref="Media.MediaKeyNotFoundException">The media key doesn't belong to this tenant.</exception>
    Task<MessageDto> SendMediaAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendMediaRequest request, CancellationToken cancellationToken);

    /// <exception cref="ConversationNotFoundException"/>
    /// <exception cref="Channels.ChannelNotConnectedException"/>
    /// <exception cref="Channels.TemplateNotFoundException"/>
    /// <exception cref="Channels.TemplateNotApprovedException"/>
    Task<MessageDto> SendTemplateAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendTemplateRequest request, CancellationToken cancellationToken);
}
