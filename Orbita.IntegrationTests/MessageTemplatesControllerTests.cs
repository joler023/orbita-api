using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Domain.Inbox;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>ORB-B07: registering templates locally and syncing their status from (a fake) Meta.</summary>
public sealed class MessageTemplatesControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public MessageTemplatesControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_ThenList_ReturnsTheTemplate()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);

        var createResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/templates", ownerCookies,
            new CreateTemplateRequest(account.Id, "greeting", MessageCategory.Utility, "es", "Hola {{1}}"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/templates", ownerCookies);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MessageTemplateSummary>>(TestRequests.JsonOptions);

        Assert.Contains(list!, t => t.MetaTemplateName == "greeting" && t.Status == TemplateStatus.Draft);
    }

    [Fact]
    public async Task Create_Duplicate_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        var request = new CreateTemplateRequest(account.Id, "greeting", MessageCategory.Utility, "es", "Hola");
        await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/templates", ownerCookies, request);

        var duplicateResponse = await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/templates", ownerCookies, request);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Sync_AppliesMetaStatusesToLocalTemplates()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, ownerCookies);
        _fixture.WhatsAppApi.Templates = [new WhatsAppTemplateInfo("greeting", "es", "APPROVED", null)];

        var syncResponse = await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/templates/sync?channelAccountId={account.Id}", ownerCookies);
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        var listResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/templates", ownerCookies);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MessageTemplateSummary>>(TestRequests.JsonOptions);

        Assert.Contains(list!, t => t.MetaTemplateName == "greeting" && t.Status == TemplateStatus.Approved);
    }

    private static async Task<ChannelAccountDto> ConnectAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var request = new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", $"waba-{Guid.NewGuid():N}", $"phone-{Guid.NewGuid():N}");
        var response = await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", cookies, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelAccountDto>(TestRequests.JsonOptions))!;
    }
}
