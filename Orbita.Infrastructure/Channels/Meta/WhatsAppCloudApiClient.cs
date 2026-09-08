using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Orbita.Application.Channels;

namespace Orbita.Infrastructure.Channels.Meta;

/// <summary>
/// WhatsApp Cloud API over Graph API (ORB-B01). Unlike Stripe/Wompi there is no
/// platform-wide API key: every call is authenticated with the connected account's own
/// token, passed per request, which is why nothing is set on DefaultRequestHeaders.
/// Request logging is removed for this client too — see <see cref="MetaAuthClient"/>.
/// </summary>
public sealed class WhatsAppCloudApiClient : IWhatsAppCloudApiClient
{
    private readonly HttpClient _httpClient;

    public WhatsAppCloudApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(configuration["Channels:Meta:GraphApiBaseUrl"] ?? MetaAuthClient.DefaultGraphApiBaseUrl);
    }

    public async Task<WhatsAppPhoneNumberInfo> GetPhoneNumberAsync(string accessToken, string phoneNumberId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, $"{Uri.EscapeDataString(phoneNumberId)}?fields=display_phone_number,verified_name", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var phone = await MetaGraphResponseReader.ReadAsync<WhatsAppPhoneNumberResponse>(response, cancellationToken);
        return new WhatsAppPhoneNumberInfo(phone.DisplayPhoneNumber, phone.VerifiedName);
    }

    public async Task SubscribeWebhookAsync(string accessToken, string wabaId, string? callbackUrl, string? verifyToken, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, $"{Uri.EscapeDataString(wabaId)}/subscribed_apps", accessToken);
        if (callbackUrl is not null && verifyToken is not null)
        {
            request.Content = JsonContent.Create(new { override_callback_uri = callbackUrl, verify_token = verifyToken });
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await MetaGraphResponseReader.ReadAsync<MetaSuccessResponse>(response, cancellationToken);
        if (!result.Success)
        {
            throw new MetaApiException("Meta reported the webhook subscription as unsuccessful.");
        }
    }

    public async Task<string> SendTextAsync(string accessToken, string phoneNumberId, string toWaId, string body, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, $"{Uri.EscapeDataString(phoneNumberId)}/messages", accessToken);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to = toWaId,
            type = "text",
            text = new { body },
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await MetaGraphResponseReader.ReadAsync<WhatsAppSendMessageResponse>(response, cancellationToken);
        return result.Messages is [{ Id: { Length: > 0 } id }, ..]
            ? id
            : throw new MetaApiException("Meta accepted the send but returned no message id.");
    }

    public async Task<string> UploadMediaAsync(string accessToken, string phoneNumberId, Stream content, string mime, string fileName, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, $"{Uri.EscapeDataString(phoneNumberId)}/media", accessToken);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("whatsapp"), "messaging_product" },
        };
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(fileContent, "file", fileName);
        request.Content = form;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await MetaGraphResponseReader.ReadAsync<WhatsAppUploadMediaResponse>(response, cancellationToken);
        return result.Id;
    }

    public async Task<string> SendMediaAsync(string accessToken, string phoneNumberId, string toWaId, string mediaTypeCategory, string mediaId, string? caption, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, $"{Uri.EscapeDataString(phoneNumberId)}/messages", accessToken);
        object mediaPayload = string.IsNullOrEmpty(caption) ? new { id = mediaId } : new { id = mediaId, caption };
        var body = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = toWaId,
            ["type"] = mediaTypeCategory,
            [mediaTypeCategory] = mediaPayload,
        };
        request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await MetaGraphResponseReader.ReadAsync<WhatsAppSendMessageResponse>(response, cancellationToken);
        return result.Messages is [{ Id: { Length: > 0 } id }, ..]
            ? id
            : throw new MetaApiException("Meta accepted the send but returned no message id.");
    }

    public async Task<string> GetMediaUrlAsync(string accessToken, string mediaId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, Uri.EscapeDataString(mediaId), accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await MetaGraphResponseReader.ReadAsync<WhatsAppMediaUrlResponse>(response, cancellationToken);
        return result.Url;
    }

    public async Task<(Stream Content, string Mime)> DownloadMediaAsync(string accessToken, string mediaUrl, CancellationToken cancellationToken)
    {
        // Meta's media URLs are absolute, on a different host than the Graph API base,
        // and still require the same bearer token to authenticate the download.
        using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
        };

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new MetaApiException($"Failed to download media (HTTP {(int)response.StatusCode}).");
        }

        var mime = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (stream, mime);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string relativeUrl, string accessToken)
        => new(method, relativeUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
        };
}
