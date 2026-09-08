namespace Orbita.Application.Inbox;

public interface IOutboundMessageService
{
    /// <exception cref="ConversationNotFoundException"/>
    /// <exception cref="Channels.ChannelNotConnectedException"/>
    Task<MessageDto> SendTextAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendTextRequest request, CancellationToken cancellationToken);
}
