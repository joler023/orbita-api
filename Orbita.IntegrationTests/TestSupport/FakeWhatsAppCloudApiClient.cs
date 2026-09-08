using System.Collections.Concurrent;
using Orbita.Application.Channels;

namespace Orbita.IntegrationTests.TestSupport;

/// <summary>
/// Stands in for the WhatsApp Cloud API (ORB-B01). Records every webhook subscription
/// it is asked for, because the per-account verify token only ever travels from the
/// backend to Meta through that call — capturing it here is how a test plays Meta's
/// part and completes the verification handshake.
/// </summary>
public sealed class FakeWhatsAppCloudApiClient : IWhatsAppCloudApiClient
{
    private readonly ConcurrentQueue<WebhookSubscription> _subscriptions = [];

    public Task<WhatsAppPhoneNumberInfo> GetPhoneNumberAsync(string accessToken, string phoneNumberId, CancellationToken cancellationToken)
        => Task.FromResult(new WhatsAppPhoneNumberInfo("+57 300 111 2233", $"Business {phoneNumberId}"));

    public Task SubscribeWebhookAsync(string accessToken, string wabaId, string? callbackUrl, string? verifyToken, CancellationToken cancellationToken)
    {
        _subscriptions.Enqueue(new WebhookSubscription(accessToken, wabaId, callbackUrl, verifyToken));
        return Task.CompletedTask;
    }

    public WebhookSubscription LatestSubscriptionFor(string wabaId) => _subscriptions.Last(s => s.WabaId == wabaId);

    public sealed record WebhookSubscription(string AccessToken, string WabaId, string? CallbackUrl, string? VerifyToken);
}
