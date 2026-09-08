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

    public Task<string> UploadMediaAsync(string accessToken, string phoneNumberId, Stream content, string mime, string fileName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var mediaId = $"media-fake-{Guid.NewGuid():N}";
        _uploadedMedia[mediaId] = (buffer.ToArray(), mime);
        return Task.FromResult(mediaId);
    }

    public Task<string> SendMediaAsync(string accessToken, string phoneNumberId, string toWaId, string mediaTypeCategory, string mediaId, string? caption, CancellationToken cancellationToken)
    {
        var errorCode = NextSendErrorCode?.Invoke(caption ?? string.Empty);
        if (errorCode is not null)
        {
            NextSendErrorCode = null;
            throw new MetaApiException($"Simulated Meta error {errorCode}.", errorCode);
        }

        var wamid = $"wamid.fake-{Guid.NewGuid():N}";
        _sentMessages.Enqueue(new SentMessage(phoneNumberId, toWaId, caption ?? string.Empty, wamid));
        return Task.FromResult(wamid);
    }

    public Task<string> GetMediaUrlAsync(string accessToken, string mediaId, CancellationToken cancellationToken)
        => Task.FromResult($"https://fake-meta-cdn.test/{mediaId}");

    public Task<(Stream Content, string Mime)> DownloadMediaAsync(string accessToken, string mediaUrl, CancellationToken cancellationToken)
    {
        var mediaId = mediaUrl[(mediaUrl.LastIndexOf('/') + 1)..];
        var (bytes, mime) = _uploadedMedia.TryGetValue(mediaId, out var stored)
            ? stored
            : (FakeInboundMediaBytes, "image/jpeg");
        return Task.FromResult<(Stream, string)>((new MemoryStream(bytes), mime));
    }

    /// <summary>Set per test to control what ListTemplatesAsync returns.</summary>
    public IReadOnlyList<WhatsAppTemplateInfo> Templates { get; set; } = [];

    public Task<IReadOnlyList<WhatsAppTemplateInfo>> ListTemplatesAsync(string accessToken, string wabaId, CancellationToken cancellationToken)
        => Task.FromResult(Templates);

    public Task<string> SendTemplateAsync(string accessToken, string phoneNumberId, string toWaId, string templateName, string language, IReadOnlyList<string> variables, CancellationToken cancellationToken)
    {
        var body = $"{templateName}({string.Join(",", variables)})";
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

    private static readonly byte[] FakeInboundMediaBytes = "fake-image-bytes"u8.ToArray();
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string Mime)> _uploadedMedia = new();

    public sealed record WebhookSubscription(string AccessToken, string WabaId, string? CallbackUrl, string? VerifyToken);

    public sealed record SentMessage(string PhoneNumberId, string ToWaId, string Body, string Wamid);
}
