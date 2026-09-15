using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>ORB-B05: sending a text message on a conversation created by ORB-B03's inbound flow.</summary>
public sealed class MessagesControllerTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public MessagesControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendText_Returns202ThenWorkerMarksSent()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (account, conversationId) = await ConnectAndReceiveInboundAsync(client, tenantId, ownerCookies);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages", ownerCookies,
            new SendTextRequest("Hola, en qué te ayudo?"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<MessageDto>(TestRequests.JsonOptions);
        Assert.Equal(MessageStatus.Queued, dto!.Status);

        await Eventually.AssertAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
            return await dbContext.Messages.AnyAsync(m => m.Id == dto.Id && m.Status == MessageStatus.Sent);
        });

        Assert.Contains(_fixture.WhatsAppApi.SentMessages, m => m.Body == "Hola, en qué te ayudo?" && m.PhoneNumberId == account.ExternalId);
    }

    [Fact]
    public async Task SendText_WhenMetaReturns131047_MessageEndsFailedWithSpanishMessage()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, conversationId) = await ConnectAndReceiveInboundAsync(client, tenantId, ownerCookies);
        _fixture.WhatsAppApi.NextSendErrorCode = _ => 131047;

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages", ownerCookies,
            new SendTextRequest("fuera de ventana"));
        var dto = await response.Content.ReadFromJsonAsync<MessageDto>(TestRequests.JsonOptions);

        await Eventually.AssertAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
            return await dbContext.Messages.AnyAsync(m => m.Id == dto!.Id && m.Status == MessageStatus.Failed);
        });

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        var message = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == dto!.Id);
        Assert.Equal("131047", message.ErrorCode);
    }

    [Fact]
    public async Task SendText_ByViewer_403()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, conversationId) = await ConnectAndReceiveInboundAsync(client, tenantId, ownerCookies);
        var (_, viewerCookies) = await TestRequests.InviteAndAcceptAsync(_fixture, client, tenantId, ownerCookies, MemberRole.Viewer);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages", viewerCookies,
            new SendTextRequest("hola"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SendText_ToConversationOfAnotherTenant_404()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant A");
        var (_, conversationId) = await ConnectAndReceiveInboundAsync(client, tenantA, cookiesA);
        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant B");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantB}/conversations/{conversationId}/messages", cookiesB,
            new SendTextRequest("hola"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(ChannelAccountDto Account, Guid ConversationId)> ConnectAndReceiveInboundAsync(HttpClient client, Guid tenantId, CookieJar cookies)
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

        return (account, conversationId);
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
