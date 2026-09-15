using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Application.Inbox;
using Orbita.Domain.Inbox;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>ORB-B08: delivery receipts for outbound messages and retrying a failed send.</summary>
public sealed class MessageStatusTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public MessageStatusTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendText_ThenStatusWebhookRead_MessageShowsReadAt()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (account, conversationId) = await ConnectAndReceiveInboundAsync(client, tenantId, ownerCookies);

        var sendResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages", ownerCookies,
            new SendTextRequest("Hola, en qué te ayudo?"));
        var dto = await sendResponse.Content.ReadFromJsonAsync<MessageDto>(TestRequests.JsonOptions);

        string externalId = null!;
        await Eventually.AssertAsync(async () =>
        {
            await using var dbContext = _fixture.CreateOwnerDbContext();
            var sent = await dbContext.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == dto!.Id && m.Status == MessageStatus.Sent);
            if (sent?.ExternalId is null)
            {
                return false;
            }

            externalId = sent.ExternalId;
            return true;
        });

        await SendWebhookAsync(client, StatusPayload(account.ExternalId, externalId, "read"));

        await Eventually.AssertAsync(async () =>
        {
            await using var dbContext = _fixture.CreateOwnerDbContext();
            var message = await dbContext.Messages.AsNoTracking().SingleAsync(m => m.Id == dto!.Id);
            return message.Status == MessageStatus.Read && message.ReadAt is not null && message.DeliveredAt is not null;
        });
    }

    [Fact]
    public async Task A_transient_failure_schedules_a_retry_instead_of_failing()
    {
        // ORB-B05 classifies 130429 as transient, and transient means "try again", not
        // "give up" — so the message stays Queued and the job goes back to Pending with an
        // attempt spent. Asserting Failed here (as this test used to) asserted the
        // opposite of the design.
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, conversationId) = await ConnectAndReceiveInboundAsync(client, tenantId, ownerCookies);
        _fixture.WhatsAppApi.NextSendErrorCode = _ => 130429;

        try
        {
            var sendResponse = await TestRequests.SendAsync(
                client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages", ownerCookies,
                new SendTextRequest("reintentable"));
            var dto = await sendResponse.Content.ReadFromJsonAsync<MessageDto>(TestRequests.JsonOptions);

            await Eventually.AssertAsync(async () =>
            {
                await using var dbContext = _fixture.CreateOwnerDbContext();
                return await dbContext.OutboundMessageJobs.AsNoTracking()
                    .AnyAsync(j => j.MessageId == dto!.Id && j.Attempts >= 1);
            });

            await using var db = _fixture.CreateOwnerDbContext();
            var job = await db.OutboundMessageJobs.AsNoTracking().SingleAsync(j => j.MessageId == dto!.Id);
            var message = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == dto!.Id);

            Assert.Equal(OutboundJobStatus.Pending, job.Status);
            Assert.True(job.NextAttemptAt > DateTimeOffset.UtcNow, "A retry has to be scheduled into the future.");
            Assert.Equal(MessageStatus.Queued, message.Status);
            Assert.Null(message.ErrorCode);
        }
        finally
        {
            // Shared fixture: leaving this set would fail every later send in this class.
            _fixture.WhatsAppApi.NextSendErrorCode = null;
        }
    }

    // Known gap: nothing covers a *successful* manual retry (POST .../messages/{id}/retry
    // returning 202 and the message reaching Sent). Getting there needs the message to be
    // Failed, which for a transient error means exhausting five automatic attempts whose
    // backoff is 30s, 60s, 120s… — minutes of real time. Forcing it by rewriting
    // next_attempt_at deadlocks against the row lock OutboundMessageWorker holds while it
    // dispatches; that attempt took eleven minutes and still failed.
    //
    // The piece that actually unblocks it is a time provider the test host can move
    // forward — the same MutableTimeProvider ORB-B06 and ORB-B07 each wrote down as
    // missing. It exists in Orbita.UnitTests/TestSupport now; wiring one into
    // TenantsApiFixture is what this test, the expired-signed-URL test and the
    // closed-service-window test are all waiting on.

    [Fact]
    public async Task Retry_Failed131047_409()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, conversationId) = await ConnectAndReceiveInboundAsync(client, tenantId, ownerCookies);
        _fixture.WhatsAppApi.NextSendErrorCode = _ => 131047;

        var sendResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages", ownerCookies,
            new SendTextRequest("fuera de ventana"));
        var dto = await sendResponse.Content.ReadFromJsonAsync<MessageDto>(TestRequests.JsonOptions);

        await Eventually.AssertAsync(async () =>
        {
            await using var dbContext = _fixture.CreateOwnerDbContext();
            return await dbContext.Messages.AsNoTracking().AnyAsync(m => m.Id == dto!.Id && m.Status == MessageStatus.Failed);
        });

        var retryResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/messages/{dto!.Id}/retry", ownerCookies);

        Assert.Equal(HttpStatusCode.Conflict, retryResponse.StatusCode);
    }

    private async Task<(ChannelAccountDto Account, Guid ConversationId)> ConnectAndReceiveInboundAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        // Connect *and* verify: a freshly connected account stays
        // PendingVerification until Meta calls the callback back, and an
        // unverified account refuses every send.
        var account = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);

        var externalId = $"wamid.{Guid.NewGuid():N}";
        var body = TextMessagePayload(account.ExternalId, "573009998877", externalId, "hola");
        await SendWebhookAsync(client, body);

        Guid conversationId = default;
        await Eventually.AssertAsync(async () =>
        {
            await using var dbContext = _fixture.CreateOwnerDbContext();
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

    private static byte[] StatusPayload(string phoneNumberId, string externalId, string status)
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
                    "statuses": [{
                      "id": "{{externalId}}", "status": "{{status}}", "timestamp": "1690000100",
                      "recipient_id": "573009998877"
                    }]
                  },
                  "field": "messages"
                }]
              }]
            }
            """);
}
