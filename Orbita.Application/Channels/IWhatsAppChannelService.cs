using Orbita.Application.Identity;

namespace Orbita.Application.Channels;

/// <summary>Connecting, listing and disconnecting a tenant's WhatsApp Business numbers (ORB-B01).</summary>
public interface IWhatsAppChannelService
{
    /// <exception cref="ForbiddenException">Caller lacks the ManageChannels permission.</exception>
    /// <exception cref="ChannelAlreadyConnectedException">The phone number belongs to another tenant.</exception>
    /// <exception cref="ChannelConnectionFailedException">Meta rejected the code or the phone number lookup.</exception>
    Task<ChannelAccountDto> ConnectAsync(Guid tenantId, Guid callerUserId, ConnectWhatsAppRequest request, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ViewChannels permission.</exception>
    Task<IReadOnlyList<ChannelAccountDto>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ViewChannels permission.</exception>
    /// <exception cref="ChannelAccountNotFoundException">No such account in this tenant.</exception>
    Task<ChannelAccountDto> GetAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken);

    /// <summary>Re-runs the webhook subscription for an account stuck in PendingVerification.</summary>
    /// <exception cref="ForbiddenException">Caller lacks the ManageChannels permission.</exception>
    /// <exception cref="ChannelAccountNotFoundException">No such account in this tenant.</exception>
    /// <exception cref="ChannelConnectionFailedException">Meta rejected the subscription again.</exception>
    Task<ChannelAccountDto> VerifyAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken);

    /// <summary>Marks the account disconnected and deletes its stored credential. Conversation history is untouched.</summary>
    /// <exception cref="ForbiddenException">Caller lacks the ManageChannels permission.</exception>
    /// <exception cref="ChannelAccountNotFoundException">No such account in this tenant.</exception>
    Task DisconnectAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken);
}
