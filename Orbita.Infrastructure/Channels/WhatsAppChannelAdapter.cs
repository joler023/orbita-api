using Orbita.Application.Channels;
using Orbita.Domain.Channels;

namespace Orbita.Infrastructure.Channels;

public sealed class WhatsAppChannelAdapter(IChannelCredentialStore credentialStore, IWhatsAppCloudApiClient whatsAppClient) : IChannelAdapter
{
    public ChannelKind Kind => ChannelKind.WhatsApp;

    public IReadOnlyList<InboundItem> ParseInbound(string payloadJson) => WhatsAppPayloadParser.Parse(payloadJson);

    public async Task<string> SendTextAsync(ChannelAccount account, string toExternalId, string body, CancellationToken cancellationToken)
    {
        var accessToken = await credentialStore.GetAsync(account.CredentialsRef, cancellationToken);

        try
        {
            return await whatsAppClient.SendTextAsync(accessToken, account.ExternalId, toExternalId, body, cancellationToken);
        }
        catch (MetaApiException exception)
        {
            var errorCode = exception.ErrorCode?.ToString() ?? "unknown";
            var (isTransient, messageEs) = MetaErrorCatalog.Describe(errorCode);
            throw new ChannelSendException(errorCode, isTransient, messageEs);
        }
    }
}
