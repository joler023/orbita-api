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

    /// <summary>
    /// The assistant's own reply (ORB-C04), sent through this same queue so it gets the
    /// persist-first guarantee, the retry policy and the per-account rate limit that a
    /// human's message gets — the acceptance criterion is explicit that the reply goes
    /// out "por la cola de salida normal, con su control de tasa".
    ///
    /// No caller id and no permission check: the sender is not a person. What stands in
    /// for authorization is that the conversation already has this agent assigned.
    /// </summary>
    /// <exception cref="ConversationNotFoundException"/>
    /// <exception cref="Channels.ChannelNotConnectedException"/>
    /// <exception cref="ServiceWindowClosedException">A free-form reply outside the 24h window would be rejected by Meta (131047).</exception>
    Task<MessageDto> SendAgentReplyAsync(Guid tenantId, Guid conversationId, string text, Guid aiRunId, CancellationToken cancellationToken);

    /// <exception cref="MessageNotFoundException"/>
    /// <exception cref="MessageNotRetryableException">The message isn't Failed, or its error wasn't transient.</exception>
    /// <exception cref="ConversationNotFoundException"/>
    /// <exception cref="Channels.ChannelNotConnectedException"/>
    Task<MessageDto> RetryAsync(Guid tenantId, Guid callerUserId, Guid messageId, CancellationToken cancellationToken);
}
