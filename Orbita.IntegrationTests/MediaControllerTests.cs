using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Application.Media;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>ORB-B06: the presigned-URL-style upload/download endpoints, end to end through the real HTTP pipeline.</summary>
public sealed class MediaControllerTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public MediaControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UploadUrl_ThenPut_ThenGet_RoundTripsBytes()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var conversationId = await ConnectAndReceiveInboundAsync(client, tenantId, ownerCookies);

        var uploadUrlResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/media/upload-url", ownerCookies,
            new CreateUploadUrlRequest("image/jpeg", "foto.jpg", 1024));
        Assert.Equal(HttpStatusCode.Created, uploadUrlResponse.StatusCode);
        var uploadUrl = await uploadUrlResponse.Content.ReadFromJsonAsync<UploadUrlDto>(TestRequests.JsonOptions);

        var bytes = "fake-jpeg-bytes"u8.ToArray();
        var putResponse = await client.PutAsync(uploadUrl!.UploadUrl, new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var getResponse = await client.GetAsync(uploadUrl.UploadUrl);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(bytes, await getResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Get_WithTamperedToken_403()
    {
        var client = TestRequests.CreateClient(_fixture);

        var response = await client.GetAsync("/api/media/not-a-real-token");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadUrl_ForOtherTenantConversation_404()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant A");
        var conversationId = await ConnectAndReceiveInboundAsync(client, tenantA, cookiesA);
        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant B");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantB}/conversations/{conversationId}/media/upload-url", cookiesB,
            new CreateUploadUrlRequest("image/jpeg", "foto.jpg", 1024));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> ConnectAndReceiveInboundAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var connectRequest = new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}");
        var connectResponse = await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", cookies, connectRequest);
        connectResponse.EnsureSuccessStatusCode();
        var account = (await connectResponse.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;

        var externalId = $"wamid.{Guid.NewGuid():N}";
        var body = TextMessagePayload(account.ExternalId, "573009998877", externalId, "hola");
        await SendWebhookAsync(client, body);

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

        return conversationId;
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
}
