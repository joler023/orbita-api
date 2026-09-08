using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

/// <summary>
/// Translates between the channel-agnostic shape the rest of the app works with and one
/// specific <see cref="ChannelKind"/>'s actual API. Inbound parsing landed in ORB-B03;
/// text sending in ORB-B05; media in ORB-B06; templates in ORB-B07.
/// </summary>
public interface IChannelAdapter
{
    ChannelKind Kind { get; }

    IReadOnlyList<InboundItem> ParseInbound(string payloadJson);

    /// <exception cref="ChannelSendException">The provider rejected the send.</exception>
    /// <returns>The provider's own id for the sent message (e.g. a wamid).</returns>
    Task<string> SendTextAsync(ChannelAccount account, string toExternalId, string body, CancellationToken cancellationToken);

    /// <summary>Uploads <paramref name="content"/> to the provider, then sends it. The caller owns <paramref name="content"/>'s lifetime.</summary>
    /// <exception cref="ChannelSendException">The provider rejected the upload or the send.</exception>
    /// <returns>The provider's own id for the sent message.</returns>
    Task<string> SendMediaAsync(ChannelAccount account, string toExternalId, Stream content, string mime, string? caption, CancellationToken cancellationToken);

    /// <summary>Fetches an inbound message's media from the provider so it can be stored locally.</summary>
    /// <exception cref="ChannelSendException">The provider rejected the download.</exception>
    Task<(Stream Content, string Mime)> DownloadMediaAsync(ChannelAccount account, string mediaExternalId, CancellationToken cancellationToken);

    /// <summary>Sends an approved template message — the only way to write outside the 24h service window.</summary>
    /// <exception cref="ChannelSendException">The provider rejected the send.</exception>
    /// <returns>The provider's own id for the sent message.</returns>
    Task<string> SendTemplateAsync(ChannelAccount account, string toExternalId, string templateName, string language, IReadOnlyList<string> variables, CancellationToken cancellationToken);
}
