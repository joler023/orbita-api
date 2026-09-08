using Orbita.Application.Channels;
using Orbita.Domain.Channels;

namespace Orbita.Infrastructure.Channels;

public sealed class WhatsAppChannelAdapter : IChannelAdapter
{
    public ChannelKind Kind => ChannelKind.WhatsApp;

    public IReadOnlyList<InboundItem> ParseInbound(string payloadJson) => WhatsAppPayloadParser.Parse(payloadJson);
}
