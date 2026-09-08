using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Channels;

public sealed class WhatsAppChannelAdapter(IChannelCredentialStore credentialStore, IWhatsAppCloudApiClient whatsAppClient) : IChannelAdapter
{
    public ChannelKind Kind => ChannelKind.WhatsApp;

    public IReadOnlyList<InboundItem> ParseInbound(string payloadJson) => WhatsAppPayloadParser.Parse(payloadJson);

    public async Task<string> SendTextAsync(ChannelAccount account, string toExternalId, string body, CancellationToken cancellationToken)
    {
        try
        {
            var accessToken = await credentialStore.GetAsync(account.CredentialsRef, cancellationToken);
            return await whatsAppClient.SendTextAsync(accessToken, account.ExternalId, toExternalId, body, cancellationToken);
        }
        catch (MetaApiException exception)
        {
            throw ToChannelSendException(exception);
        }
    }

    public async Task<string> SendMediaAsync(ChannelAccount account, string toExternalId, Stream content, string mime, string? caption, CancellationToken cancellationToken)
    {
        try
        {
            var accessToken = await credentialStore.GetAsync(account.CredentialsRef, cancellationToken);
            var fileName = $"file.{MediaMimeCatalog.ExtensionFor(mime)}";
            var mediaId = await whatsAppClient.UploadMediaAsync(accessToken, account.ExternalId, content, mime, fileName, cancellationToken);
            return await whatsAppClient.SendMediaAsync(accessToken, account.ExternalId, toExternalId, MediaTypeCategory(mime), mediaId, caption, cancellationToken);
        }
        catch (MetaApiException exception)
        {
            throw ToChannelSendException(exception);
        }
    }

    public async Task<(Stream Content, string Mime)> DownloadMediaAsync(ChannelAccount account, string mediaExternalId, CancellationToken cancellationToken)
    {
        try
        {
            var accessToken = await credentialStore.GetAsync(account.CredentialsRef, cancellationToken);
            var mediaUrl = await whatsAppClient.GetMediaUrlAsync(accessToken, mediaExternalId, cancellationToken);
            return await whatsAppClient.DownloadMediaAsync(accessToken, mediaUrl, cancellationToken);
        }
        catch (MetaApiException exception)
        {
            throw ToChannelSendException(exception);
        }
    }

    public async Task<string> SendTemplateAsync(ChannelAccount account, string toExternalId, string templateName, string language, IReadOnlyList<string> variables, CancellationToken cancellationToken)
    {
        try
        {
            var accessToken = await credentialStore.GetAsync(account.CredentialsRef, cancellationToken);
            return await whatsAppClient.SendTemplateAsync(accessToken, account.ExternalId, toExternalId, templateName, language, variables, cancellationToken);
        }
        catch (MetaApiException exception)
        {
            throw ToChannelSendException(exception);
        }
    }

    private static ChannelSendException ToChannelSendException(MetaApiException exception)
    {
        var errorCode = exception.ErrorCode?.ToString() ?? "unknown";
        var (isTransient, messageEs) = MetaErrorCatalog.Describe(errorCode);
        return new ChannelSendException(errorCode, isTransient, messageEs);
    }

    private static string MediaTypeCategory(string mime) => mime.Split('/')[0] switch
    {
        "image" => "image",
        "audio" => "audio",
        "video" => "video",
        _ => "document",
    };
}
