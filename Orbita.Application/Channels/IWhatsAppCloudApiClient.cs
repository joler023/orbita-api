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
}

/// <param name="DisplayPhoneNumber">As Meta formats it, e.g. "+57 300 1112233".</param>
/// <param name="VerifiedName">The business name Meta verified for this number.</param>
public sealed record WhatsAppPhoneNumberInfo(string DisplayPhoneNumber, string VerifiedName);
