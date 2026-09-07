using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Crm;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class ContactsControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public ContactsControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_ThenGet_ReturnsTheContact()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var created = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/contacts",
            cookies,
            new CreateContactRequest("Ana Pérez", "+57 300 111 2233", null, "ana@shop.com", "whatsapp", null));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var contact = await created.Content.ReadFromJsonAsync<ContactDetail>(TestRequests.JsonOptions);
        Assert.Equal("Ana Pérez", contact!.DisplayName);
        Assert.Equal("+573001112233", contact.Phone);

        var fetched = await TestRequests.SendAsync(
            client,
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/contacts/{contact.Id}",
            cookies);
        var detail = await fetched.Content.ReadFromJsonAsync<ContactDetail>(TestRequests.JsonOptions);
        Assert.Equal(contact.Id, detail!.Id);
    }

    [Fact]
    public async Task Create_DuplicatePhone_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/contacts",
            cookies,
            new CreateContactRequest("Ana", "+573001112233", null, null, null, null));

        var duplicate = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/contacts",
            cookies,
            new CreateContactRequest("Otra", "+57 300 111 2233", null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task CreateOpportunity_WithContact_ShowsOnContactList()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var created = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/contacts",
            cookies,
            new CreateContactRequest("Ana", "+573009998877", null, null, "whatsapp", null));
        var contact = await created.Content.ReadFromJsonAsync<ContactDetail>(TestRequests.JsonOptions);

        var pipelinesResponse = await TestRequests.SendAsync(
            client,
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/pipelines",
            cookies);
        var pipelines = await pipelinesResponse.Content.ReadFromJsonAsync<List<PipelineSummary>>(TestRequests.JsonOptions);
        var pipeline = pipelines![0];

        await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/opportunities",
            cookies,
            new CreateOpportunityRequest("Sitio web", 2_000m, pipeline.Stages[0].Id, null, contact!.Id));

        var list = await TestRequests.SendAsync(
            client,
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/contacts",
            cookies);
        var rows = await list.Content.ReadFromJsonAsync<List<ContactListItem>>(TestRequests.JsonOptions);
        var row = Assert.Single(rows!);
        Assert.Equal(pipeline.Stages[0].Name, row.StageName);
        Assert.Equal(2_000m, row.Amount);
    }
}
