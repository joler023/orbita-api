using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orbita.Application.Channels;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>ORB-B04: staged events actually reach the outbox and get published; the app connection can never delete a published row.</summary>
public sealed class OutboxTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public OutboxTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InboundMessage_ProducesPublishedOutboxRow()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        var externalId = $"wamid.{Guid.NewGuid():N}";
        var body = TextMessagePayload(account.ExternalId, "573009998877", externalId, "hola");

        await SendWebhookAsync(client, body);

        await Eventually.AssertAsync(() => Task.FromResult(
            _fixture.IntegrationEvents.Received.Any(e => e.EventType == "message.received")
            && _fixture.IntegrationEvents.Received.Any(e => e.EventType == "conversation.opened")));

        var messageEvent = _fixture.IntegrationEvents.Received.First(e => e.EventType == "message.received");
        Assert.DoesNotContain("hola", messageEvent.PayloadJson);
        Assert.DoesNotContain("573009998877", messageEvent.PayloadJson);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        await Eventually.AssertAsync(async () =>
            await dbContext.Set<Orbita.Domain.Outbox.OutboxEvent>().AnyAsync(e => e.Id == messageEvent.Id && e.PublishedAt != null));
    }

    [Fact]
    public async Task OrbitaApp_CannotDeleteOutboxRows()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => dbContext.Database.ExecuteSqlRawAsync("DELETE FROM outbox_events"));

        Assert.Equal("42501", exception.SqlState);
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

    private static async Task<ChannelAccountDto> ConnectAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var request = new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}");
        var response = await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", cookies, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;
    }
}
