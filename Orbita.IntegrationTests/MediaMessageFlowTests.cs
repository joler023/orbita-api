using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Application.Inbox;
using Orbita.Application.Media;
using Orbita.Domain.Inbox;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>ORB-B06 end to end: an inbound image gets downloaded and stored; an outbound media message gets uploaded to Meta then sent.</summary>
public sealed class MediaMessageFlowTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public MediaMessageFlowTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InboundImage_StoresFileAndExposesSignedUrl()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var connectResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", ownerCookies,
            new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}"));
        var account = (await connectResponse.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;

        var externalId = $"wamid.{Guid.NewGuid():N}";
        var body = ImageMessagePayload(account.ExternalId, "573009998877", externalId, "media-fake-123");
        await SendWebhookAsync(client, body);

        Guid messageId = default;
        await Eventually.AssertAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
            var message = await dbContext.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalId == externalId);
            if (message?.MediaKey is null)
            {
                return false;
            }

            messageId = message.Id;
            return true;
        });

        var mediaUrlResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/messages/{messageId}/media-url", ownerCookies);
        mediaUrlResponse.EnsureSuccessStatusCode();
        var mediaUrl = await mediaUrlResponse.Content.ReadFromJsonAsync<MediaUrlDto>(TestRequests.JsonOptions);

        var downloadResponse = await client.GetAsync(mediaUrl!.Url);
        downloadResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SendMedia_UploadsToMetaThenSends()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var connectResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", ownerCookies,
            new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}"));
        var account = (await connectResponse.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;

        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573009998877", externalId, "hola"));
        Guid conversationId = default;
        await Eventually.AssertAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
            var message = await dbContext.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalId == externalId);
            if (message is null)
            {
                return false;
            }

            conversationId = message.ConversationId;
            return true;
        });

        var uploadUrlResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/media/upload-url", ownerCookies,
            new CreateUploadUrlRequest("image/jpeg", "foto.jpg", 1024));
        var uploadUrl = await uploadUrlResponse.Content.ReadFromJsonAsync<UploadUrlDto>(TestRequests.JsonOptions);
        await client.PutAsync(uploadUrl!.UploadUrl, new ByteArrayContent("fake-jpeg-bytes"u8.ToArray()));

        var sendResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages/media", ownerCookies,
            new SendMediaRequest(uploadUrl.Key, "image/jpeg", "mira esto"));
        var dto = await sendResponse.Content.ReadFromJsonAsync<MessageDto>(TestRequests.JsonOptions);

        await Eventually.AssertAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
            return await dbContext.Messages.AnyAsync(m => m.Id == dto!.Id && m.Status == MessageStatus.Sent);
        });

        Assert.Contains(_fixture.WhatsAppApi.SentMessages, m => m.Body == "mira esto");
    }

    private static async Task SendWebhookAsync(HttpClient client, byte[] body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Content.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(body));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static string Sign(byte[] body)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), body)).ToLowerInvariant();

    private static byte[] TextMessagePayload(string phoneNumberId, string fromWaId, string externalId, string text)
        => Encoding.UTF8.GetBytes(
            $$"""
            {
              "object": "whatsapp_business_account",
              "entry": [{
                "id": "waba-1",
                "changes": [{
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "573001112233", "phone_number_id": "{{phoneNumberId}}" },
                    "contacts": [{ "profile": { "name": "Cliente" }, "wa_id": "{{fromWaId}}" }],
                    "messages": [{
                      "from": "{{fromWaId}}", "id": "{{externalId}}", "timestamp": "1690000000", "type": "text",
                      "text": { "body": "{{text}}" }
                    }]
                  },
                  "field": "messages"
                }]
              }]
            }
            """);

    private static byte[] ImageMessagePayload(string phoneNumberId, string fromWaId, string externalId, string mediaId)
        => Encoding.UTF8.GetBytes(
            $$"""
            {
              "object": "whatsapp_business_account",
              "entry": [{
                "id": "waba-1",
                "changes": [{
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "573001112233", "phone_number_id": "{{phoneNumberId}}" },
                    "contacts": [{ "profile": { "name": "Cliente" }, "wa_id": "{{fromWaId}}" }],
                    "messages": [{
                      "from": "{{fromWaId}}", "id": "{{externalId}}", "timestamp": "1690000000", "type": "image",
                      "image": { "id": "{{mediaId}}", "mime_type": "image/jpeg" }
                    }]
                  },
                  "field": "messages"
                }]
              }]
            }
            """);
}
