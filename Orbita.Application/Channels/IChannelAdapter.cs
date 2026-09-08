using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

/// <summary>
/// Translates between the channel-agnostic shape the rest of the app works with and one
/// specific <see cref="ChannelKind"/>'s actual API. Inbound parsing landed in ORB-B03;
/// text sending in ORB-B05. Media/templates get their own methods once those stories
/// know the real shape — no point guessing it here.
/// </summary>
public interface IChannelAdapter
{
    ChannelKind Kind { get; }

    IReadOnlyList<InboundItem> ParseInbound(string payloadJson);

    /// <exception cref="ChannelSendException">The provider rejected the send.</exception>
    /// <returns>The provider's own id for the sent message (e.g. a wamid).</returns>
    Task<string> SendTextAsync(ChannelAccount account, string toExternalId, string body, CancellationToken cancellationToken);
}
