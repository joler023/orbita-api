using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Domain.Identity;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>
/// ORB-B01 end to end: connect a WhatsApp number through the fake Graph API, then play
/// Meta's part in the webhook verification handshake using the verify token the
/// backend handed to the (fake) subscription call.
/// </summary>
public sealed class ChannelsControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public ChannelsControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConnectWhatsApp_ThenVerifyCallback_LeavesTheAccountConnected()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var request = NewConnectRequest();

        var connectResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", ownerCookies, request);

        Assert.Equal(HttpStatusCode.Created, connectResponse.StatusCode);
        var pending = await connectResponse.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions);
        Assert.Equal(ChannelStatus.PendingVerification, pending!.Status);
        Assert.Equal(ChannelKind.WhatsApp, pending.Kind);
        Assert.Equal(request.PhoneNumberId, pending.ExternalId);
        Assert.Equal("+573001112233", pending.PhoneE164);
        Assert.Equal($"Business {request.PhoneNumberId}", pending.DisplayName);
        Assert.False(pending.ExpiresSoon);

        // The backend registered a per-account callback with Meta; Meta now calls it back.
        var subscription = _fixture.WhatsAppApi.LatestSubscriptionFor(request.WabaId);
        Assert.Equal($"{TenantsApiFixture.WebhookPublicBaseUrl}/api/webhooks/whatsapp/{pending.Id:D}", subscription.CallbackUrl);
        Assert.Equal(FakeMetaAuthClient.TokenFor(request.Code), subscription.AccessToken);
        Assert.NotNull(subscription.VerifyToken);

        var verifyResponse = await client.GetAsync(
            $"/api/webhooks/whatsapp/{pending.Id}?hub.mode=subscribe&hub.verify_token={subscription.VerifyToken}&hub.challenge=challenge-42");

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.Equal("challenge-42", await verifyResponse.Content.ReadAsStringAsync());

        var getResponse = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/channels/{pending.Id}", ownerCookies);
        var connected = await getResponse.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions);
        Assert.Equal(ChannelStatus.Connected, connected!.Status);
        Assert.NotNull(connected.ConnectedAt);
    }

    [Fact]
    public async Task VerifyCallback_WithTheWrongAccountToken_IsForbiddenAndLeavesTheAccountPending()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var pending = await ConnectAsync(client, tenantId, ownerCookies);

        var verifyResponse = await client.GetAsync(
            $"/api/webhooks/whatsapp/{pending.Id}?hub.mode=subscribe&hub.verify_token=not-the-secret&hub.challenge=x");

        Assert.Equal(HttpStatusCode.Forbidden, verifyResponse.StatusCode);
        var getResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/channels/{pending.Id}", ownerCookies);
        var account = await getResponse.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions);
        Assert.Equal(ChannelStatus.PendingVerification, account!.Status);
    }

    [Fact]
    public async Task VerifyAppLevelCallback_UsesTheConfiguredGlobalToken()
    {
        var client = TestRequests.CreateClient(_fixture);

        var ok = await client.GetAsync(
            $"/api/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={TenantsApiFixture.GlobalVerifyToken}&hub.challenge=hello");
        var forbidden = await client.GetAsync(
            "/api/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=hello");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("hello", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task ConnectWhatsApp_ByAnAgent_ReturnsForbidden()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, agentCookies) = await TestRequests.InviteAndAcceptAsync(_fixture, client, tenantId, ownerCookies, MemberRole.Agent);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", agentCookies, NewConnectRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ByAnAgent_IsAllowedButOnlyShowsTheirOwnTenantsAccounts()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Acme Corp");
        var mine = await ConnectAsync(client, tenantId, ownerCookies);
        var (_, _, otherTenantId, otherCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Other Business");
        await ConnectAsync(client, otherTenantId, otherCookies);
        var (_, agentCookies) = await TestRequests.InviteAndAcceptAsync(_fixture, client, tenantId, ownerCookies, MemberRole.Agent);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/channels", agentCookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await response.Content.ReadFromJsonAsync<List<ChannelAccountDto>>(TestRequests.JsonOptions);
        var only = Assert.Single(accounts!);
        Assert.Equal(mine.Id, only.Id);
    }

    [Fact]
    public async Task Get_AnAccountOfAnotherTenant_ReturnsNotFound()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant A");
        var accountA = await ConnectAsync(client, tenantA, cookiesA);
        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant B");

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantB}/channels/{accountA.Id}", cookiesB);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConnectWhatsApp_WithANumberAlreadyConnectedElsewhere_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant A");
        var request = NewConnectRequest();
        await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantA}/channels/whatsapp", cookiesA, request);
        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Tenant B");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantB}/channels/whatsapp", cookiesB, request with { Code = "another-code" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Disconnect_MarksTheAccountDisconnectedAndDeletesItsStoredCredential()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        Assert.Equal(1, await CountCredentialRowsAsync(account.Id));

        var response = await TestRequests.SendAsync(client, HttpMethod.Delete, $"/api/tenants/{tenantId}/channels/{account.Id}", ownerCookies);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/channels/{account.Id}", ownerCookies);
        var disconnected = await getResponse.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions);
        Assert.Equal(ChannelStatus.Disconnected, disconnected!.Status);
        Assert.Equal(0, await CountCredentialRowsAsync(account.Id));
    }

    private static ConnectWhatsAppRequest NewConnectRequest()
        => new($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}");

    private static async Task<ChannelAccountDto> ConnectAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", cookies, NewConnectRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;
    }

    /// <summary>Counts credential rows referenced by the account — neither table is RLS'd, so the app connection sees them.</summary>
    private async Task<int> CountCredentialRowsAsync(Guid channelAccountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        var account = await dbContext.ChannelAccounts.AsNoTracking().SingleAsync(a => a.Id == channelAccountId);
        var credentialId = Guid.Parse(account.CredentialsRef["local://".Length..]);
        return await dbContext.ChannelCredentials.CountAsync(c => c.Id == credentialId);
    }
}
