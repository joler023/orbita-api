using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

/// <summary>
/// Translates a raw webhook payload for one <see cref="ChannelKind"/> into the
/// channel-agnostic <see cref="InboundItem"/> shape the inbound processor works with
/// (ORB-B03). Outbound sending gets its own port once ORB-B05 knows its actual shape —
/// no point guessing it here.
/// </summary>
public interface IChannelAdapter
{
    ChannelKind Kind { get; }

    IReadOnlyList<InboundItem> ParseInbound(string payloadJson);
}
