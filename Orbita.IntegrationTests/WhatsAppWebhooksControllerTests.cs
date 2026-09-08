using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>
/// ORB-B02 end to end: a signed WhatsApp webhook POST lands one row in
/// inbound_webhook_events; an unsigned/mis-signed one is rejected before it ever
/// reaches the queue; a redelivered payload never produces a second row.
/// </summary>
public sealed class WhatsAppWebhooksControllerTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public WhatsAppWebhooksControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Post_WithValidHmac_Returns200AndPersistsQueueRow()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        var body = WhatsAppPayload(account.ExternalId);

        var response = await SendWebhookAsync(client, "/api/webhooks/whatsapp", body, Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountQueueRowsAsync(account.Id));
    }

    [Fact]
    public async Task Post_WithBadHmac_Returns401AndPersistsNothing()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        var body = WhatsAppPayload(account.ExternalId);

        var response = await SendWebhookAsync(client, "/api/webhooks/whatsapp", body, "sha256=" + new string('0', 64));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountQueueRowsAsync(account.Id));
    }

    [Fact]
    public async Task Post_SamePayloadTwice_PersistsOneRow()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        var body = WhatsAppPayload(account.ExternalId);
        var signature = Sign(body);

        var first = await SendWebhookAsync(client, "/api/webhooks/whatsapp", body, signature);
        var second = await SendWebhookAsync(client, "/api/webhooks/whatsapp", body, signature);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, await CountQueueRowsAsync(account.Id));
    }

    [Fact]
    public async Task Post_ForAnUnknownAccount_Returns200ButPersistsNothing()
    {
        var client = TestRequests.CreateClient(_fixture);
        var body = WhatsAppPayload("no-such-phone-number-id");

        var response = await SendWebhookAsync(client, "/api/webhooks/whatsapp", body, Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        Assert.Equal(0, await dbContext.InboundWebhookEvents.CountAsync());
    }

    [Fact]
    public async Task Get_Verify_WithGlobalToken_ReturnsChallenge()
    {
        var client = TestRequests.CreateClient(_fixture);

        var response = await client.GetAsync(
            $"/api/webhooks/instagram?hub.mode=subscribe&hub.verify_token={TenantsApiFixture.GlobalVerifyToken}&hub.challenge=ig-42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ig-42", await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, string url, byte[] body, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("X-Hub-Signature-256", signature);
        return await client.SendAsync(request);
    }

    private static string Sign(byte[] body)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), body)).ToLowerInvariant();

    private static byte[] WhatsAppPayload(string phoneNumberId)
        => Encoding.UTF8.GetBytes(
            "{\"object\":\"whatsapp_business_account\",\"entry\":[{\"id\":\"waba-1\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\""
            + phoneNumberId + "\"}}}]}]}");

    private static async Task<ChannelAccountDto> ConnectAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var request = new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}");
        var response = await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", cookies, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;
    }

    private async Task<int> CountQueueRowsAsync(Guid channelAccountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        return await dbContext.InboundWebhookEvents.CountAsync(e => e.ChannelAccountId == channelAccountId);
    }
}
