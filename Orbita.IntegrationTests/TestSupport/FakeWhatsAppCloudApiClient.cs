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
    private readonly ConcurrentQueue<SentMessage> _sentMessages = [];

    /// <summary>Override per test to simulate a Meta error code on the next SendTextAsync call, e.g. `_ => 131047`.</summary>
    public Func<string, int?>? NextSendErrorCode { get; set; }

    public Task<WhatsAppPhoneNumberInfo> GetPhoneNumberAsync(string accessToken, string phoneNumberId, CancellationToken cancellationToken)
        => Task.FromResult(new WhatsAppPhoneNumberInfo("+57 300 111 2233", $"Business {phoneNumberId}"));

    public Task SubscribeWebhookAsync(string accessToken, string wabaId, string? callbackUrl, string? verifyToken, CancellationToken cancellationToken)
    {
        _subscriptions.Enqueue(new WebhookSubscription(accessToken, wabaId, callbackUrl, verifyToken));
        return Task.CompletedTask;
    }

    public Task<string> SendTextAsync(string accessToken, string phoneNumberId, string toWaId, string body, CancellationToken cancellationToken)
    {
        var errorCode = NextSendErrorCode?.Invoke(body);
        if (errorCode is not null)
        {
            NextSendErrorCode = null;
            throw new MetaApiException($"Simulated Meta error {errorCode}.", errorCode);
        }

        var wamid = $"wamid.fake-{Guid.NewGuid():N}";
        _sentMessages.Enqueue(new SentMessage(phoneNumberId, toWaId, body, wamid));
        return Task.FromResult(wamid);
    }

    public WebhookSubscription LatestSubscriptionFor(string wabaId) => _subscriptions.Last(s => s.WabaId == wabaId);

    public IReadOnlyCollection<SentMessage> SentMessages => _sentMessages;

    public sealed record WebhookSubscription(string AccessToken, string WabaId, string? CallbackUrl, string? VerifyToken);

    public sealed record SentMessage(string PhoneNumberId, string ToWaId, string Body, string Wamid);
}
