namespace Orbita.Application.Channels;

/// <summary>
/// The WhatsApp Cloud API calls the connection flow needs (ORB-B01). Later stories add
/// sending (ORB-B05), media (ORB-B06) and templates (ORB-B07) to this same port —
/// keep every method a thin, typed wrapper over one Graph API call.
/// </summary>
public interface IWhatsAppCloudApiClient
{
    /// <exception cref="MetaApiException">The phone number id does not exist or the token cannot see it.</exception>
    Task<WhatsAppPhoneNumberInfo> GetPhoneNumberAsync(string accessToken, string phoneNumberId, CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes our app to the WABA's webhooks. When <paramref name="callbackUrl"/> and
    /// <paramref name="verifyToken"/> are given, Meta registers them as this WABA's own
    /// callback (override_callback_uri) and verifies the URL synchronously during the
    /// call — so the account row that answers that verification must already be
    /// committed before calling this.
    /// </summary>
    /// <exception cref="MetaApiException">Subscription or callback verification failed.</exception>
    Task SubscribeWebhookAsync(string accessToken, string wabaId, string? callbackUrl, string? verifyToken, CancellationToken cancellationToken);

    /// <summary>ORB-B05: sends a free-form text message.</summary>
    /// <returns>The wamid Meta assigned to the sent message.</returns>
    /// <exception cref="MetaApiException">Meta rejected the send — <see cref="MetaApiException.ErrorCode"/> carries its numeric error.code.</exception>
    Task<string> SendTextAsync(string accessToken, string phoneNumberId, string toWaId, string body, CancellationToken cancellationToken);

    /// <summary>ORB-B06: uploads a media binary — the first half of sending media, Meta needs the file before it can reference it in a message.</summary>
    /// <returns>Meta's own media id.</returns>
    /// <exception cref="MetaApiException"/>
    Task<string> UploadMediaAsync(string accessToken, string phoneNumberId, Stream content, string mime, string fileName, CancellationToken cancellationToken);

    /// <returns>The wamid Meta assigned to the sent message.</returns>
    /// <exception cref="MetaApiException"/>
    Task<string> SendMediaAsync(string accessToken, string phoneNumberId, string toWaId, string mediaTypeCategory, string mediaId, string? caption, CancellationToken cancellationToken);

    /// <summary>Resolves an inbound message's media id to a short-lived, authenticated download URL.</summary>
    /// <exception cref="MetaApiException"/>
    Task<string> GetMediaUrlAsync(string accessToken, string mediaId, CancellationToken cancellationToken);

    /// <exception cref="MetaApiException"/>
    Task<(Stream Content, string Mime)> DownloadMediaAsync(string accessToken, string mediaUrl, CancellationToken cancellationToken);
}

/// <param name="DisplayPhoneNumber">As Meta formats it, e.g. "+57 300 1112233".</param>
/// <param name="VerifiedName">The business name Meta verified for this number.</param>
public sealed record WhatsAppPhoneNumberInfo(string DisplayPhoneNumber, string VerifiedName);
