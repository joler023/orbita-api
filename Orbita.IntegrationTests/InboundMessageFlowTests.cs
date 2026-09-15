using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Domain.Inbox;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>
/// ORB-B03 end to end: a signed WhatsApp webhook POST lands on the queue (ORB-B02),
/// InboundMessageWorker picks it up, and a contact/conversation/message end up visible
/// through the same OrbitaDbContext the app uses — no direct call into the processor.
/// </summary>
public sealed class InboundMessageFlowTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public InboundMessageFlowTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostWebhook_ThenWorker_CreatesConversationVisibleInDb()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        var externalId = $"wamid.{Guid.NewGuid():N}";
        var body = TextMessagePayload(account.ExternalId, "573009998877", externalId, "hola, necesito ayuda");

        await SendWebhookAsync(client, body);

        await Eventually.AssertAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
            return await dbContext.Messages.AnyAsync(m => m.ExternalId == externalId);
        });

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        var message = await db.Messages.AsNoTracking().SingleAsync(m => m.ExternalId == externalId);
        var conversation = await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == message.ConversationId);
        var contact = await db.Contacts.AsNoTracking().SingleAsync(c => c.Id == conversation.ContactId);

        Assert.Equal("hola, necesito ayuda", message.Body);
        Assert.Equal(account.Id, conversation.ChannelAccountId);
        Assert.Equal("573009998877", contact.Phone);
        Assert.Equal(1, conversation.UnreadCount);
    }

    [Fact]
    public async Task PostSameMessageTwice_CreatesOneMessage()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        var externalId = $"wamid.{Guid.NewGuid():N}";
        var body = TextMessagePayload(account.ExternalId, "573009998877", externalId, "hola");

        await SendWebhookAsync(client, body);
        await Eventually.AssertAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
            return await dbContext.Messages.CountAsync(m => m.ExternalId == externalId) == 1;
        });

        // A redelivery of the exact same payload is de-duped at the webhook queue level
        // (ORB-B02); this second signed POST with a *different* wrapping still carries
        // the same message externalId, so the processor's own idempotency check is what
        // must hold here even if it reaches the queue.
        await SendWebhookAsync(client, body, "wamid.wrapper-2");

        await Task.Delay(TimeSpan.FromSeconds(3));
        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ExternalId == externalId));
    }

    private static async Task SendWebhookAsync(HttpClient client, byte[] body, string? uniqueSuffix = null)
    {
        var payloadBytes = uniqueSuffix is null ? body : AppendNoise(body, uniqueSuffix);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp") { Content = new ByteArrayContent(payloadBytes) };
        request.Content.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(payloadBytes));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static byte[] AppendNoise(byte[] originalJsonBody, string suffix)
    {
        // Cheap way to get a byte-distinct (different payload_hash) envelope carrying
        // the same inner message externalId, simulating Meta re-wrapping a redelivery.
        var text = Encoding.UTF8.GetString(originalJsonBody);
        return Encoding.UTF8.GetBytes(text.Replace("\"waba-1\"", $"\"waba-1-{suffix}\""));
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

    private static async Task<ChannelAccountDto> ConnectAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var request = new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}");
        var response = await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", cookies, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;
    }
}
