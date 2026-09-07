using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Ai;
using Orbita.Application.Common;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C02 end to end, against a real Postgres with Row Level Security actually applying
/// (the app connects as <c>orbita_app</c>, which RLS binds). Only the model provider is
/// faked.
/// </summary>
public sealed class KnowledgeBaseApiTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    [Fact]
    public async Task Registering_an_organization_seeds_one_disabled_assistant()
    {
        // ORB-D04's "nobody starts on an empty screen", applied to assistants.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, _) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Panadería Aurora");

        var agent = await GetSeededAgentAsync(tenantId);

        Assert.False(agent.IsEnabled);
        Assert.Contains("Panadería Aurora", agent.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pasted_note_is_accepted_then_indexed_into_searchable_chunks()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        var accepted = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/knowledge/text",
            cookies,
            new { title = "Horarios", text = "Abrimos de lunes a sábado de 7am a 7pm.\n\nDomingos cerrado." });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var document = await accepted.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(TestRequests.JsonOptions);

        // Accepted, not indexed: the work happens in the background.
        Assert.NotNull(document);
        Assert.Equal(KnowledgeDocStatus.Pending, document.Status);
        Assert.Equal(0, document.ChunkCount);

        await RunIndexerAsync();

        var indexed = await GetDocumentAsync(client, cookies, tenantId, agent.Id, document.Id);
        Assert.Equal(KnowledgeDocStatus.Indexed, indexed.Status);
        Assert.True(indexed.ChunkCount > 0);
        Assert.NotNull(indexed.IndexedAt);
        Assert.Null(indexed.FailureReason);
    }

    [Fact]
    public async Task An_uploaded_markdown_file_is_indexed_the_same_way()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        var response = await UploadAsync(client, cookies, tenantId, agent.Id, "catalogo.md", "# Catálogo\n\nPan artesanal: 5000 COP.");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(TestRequests.JsonOptions);
        Assert.Equal("catalogo", document!.Title);

        await RunIndexerAsync();

        var indexed = await GetDocumentAsync(client, cookies, tenantId, agent.Id, document.Id);
        Assert.Equal(KnowledgeDocStatus.Indexed, indexed.Status);
    }

    [Fact]
    public async Task An_unsupported_file_type_is_refused_before_anything_is_stored()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        var response = await UploadAsync(client, cookies, tenantId, agent.Id, "hoja.xlsx", "no importa");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await ListAsync(client, cookies, tenantId, agent.Id)).Items);
    }

    [Fact]
    public async Task A_document_that_cannot_be_read_fails_with_a_reason_in_spanish()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        // A .pdf whose bytes are not a PDF at all.
        var response = await UploadAsync(client, cookies, tenantId, agent.Id, "roto.pdf", "esto no es un pdf");
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(TestRequests.JsonOptions);

        await RunIndexerAsync();

        var failed = await GetDocumentAsync(client, cookies, tenantId, agent.Id, document!.Id);
        Assert.Equal(KnowledgeDocStatus.Failed, failed.Status);
        Assert.NotNull(failed.FailureReason);
        Assert.DoesNotContain("Exception", failed.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_provider_outage_leaves_the_document_pending_for_the_next_pass()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        var response = await UploadAsync(client, cookies, tenantId, agent.Id, "notas.txt", "Vendemos pan artesanal.");
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(TestRequests.JsonOptions);

        fixture.Llm.NextFailure = new LlmProviderException("fake", "connection refused", isTransient: true);
        await RunIndexerAsync();

        var stillPending = await GetDocumentAsync(client, cookies, tenantId, agent.Id, document!.Id);
        Assert.Equal(KnowledgeDocStatus.Pending, stillPending.Status);
        Assert.Null(stillPending.FailureReason);

        // And it succeeds once the provider is back.
        await RunIndexerAsync();
        Assert.Equal(
            KnowledgeDocStatus.Indexed,
            (await GetDocumentAsync(client, cookies, tenantId, agent.Id, document.Id)).Status);
    }

    [Fact]
    public async Task Reindexing_replaces_the_chunks_rather_than_adding_a_second_set()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        var response = await UploadAsync(client, cookies, tenantId, agent.Id, "notas.txt", "Vendemos pan artesanal.");
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(TestRequests.JsonOptions);
        await RunIndexerAsync();

        var first = await GetDocumentAsync(client, cookies, tenantId, agent.Id, document!.Id);

        var reindex = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/knowledge/{document.Id}/reindex", cookies);
        Assert.Equal(HttpStatusCode.Accepted, reindex.StatusCode);

        await RunIndexerAsync();
        var second = await GetDocumentAsync(client, cookies, tenantId, agent.Id, document.Id);

        Assert.Equal(KnowledgeDocStatus.Indexed, second.Status);
        Assert.Equal(first.ChunkCount, second.ChunkCount);
        Assert.Equal(first.ChunkCount, await CountChunksAsync(tenantId, document.Id));
    }

    [Fact]
    public async Task Deleting_a_document_removes_its_chunks_too()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        var response = await UploadAsync(client, cookies, tenantId, agent.Id, "notas.txt", "Vendemos pan artesanal.");
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(TestRequests.JsonOptions);
        await RunIndexerAsync();

        var deleted = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/knowledge/{document!.Id}", cookies);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await CountChunksAsync(tenantId, document.Id));
    }

    [Fact]
    public async Task One_tenant_can_never_touch_another_tenants_documents()
    {
        // ORB-A09 applied to ORB-C02's tables: "un fallo aquí no es un bug, es el fin del
        // producto".
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = await GetSeededAgentAsync(tenantA);

        var response = await UploadAsync(client, cookiesA, tenantA, agentA.Id, "secreto.txt", "Margen de ganancia: 40%.");
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>(TestRequests.JsonOptions);
        await RunIndexerAsync();

        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var deleteAcrossTenants = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantB}/ai-agents/{agentA.Id}/knowledge/{document!.Id}", cookiesB);
        Assert.Equal(HttpStatusCode.NotFound, deleteAcrossTenants.StatusCode);

        var reindexAcrossTenants = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantB}/ai-agents/{agentA.Id}/knowledge/{document.Id}/reindex", cookiesB);
        Assert.Equal(HttpStatusCode.NotFound, reindexAcrossTenants.StatusCode);

        // Tenant B's own list never contains it either.
        var agentB = await GetSeededAgentAsync(tenantB);
        var listB = await ListAsync(client, cookiesB, tenantB, agentB.Id);
        Assert.DoesNotContain(listB.Items, item => item.Id == document.Id);

        // And tenant A's document is untouched.
        Assert.Equal(
            KnowledgeDocStatus.Indexed,
            (await GetDocumentAsync(client, cookiesA, tenantA, agentA.Id, document.Id)).Status);
    }

    [Fact]
    public async Task A_caller_outside_the_tenant_is_refused()
    {
        // ORB-A08: hiding a button is not access control.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, _) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = await GetSeededAgentAsync(tenantA);

        var (_, _, _, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantA}/ai-agents/{agentA.Id}/knowledge", cookiesB);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_document_list_pages_with_a_cursor()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        for (var i = 0; i < 5; i++)
        {
            await TestRequests.SendAsync(
                client,
                HttpMethod.Post,
                $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/knowledge/text",
                cookies,
                new { title = $"Nota {i}", text = $"Contenido número {i}." });
        }

        var firstPage = await ListAsync(client, cookies, tenantId, agent.Id, limit: 2);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await ListAsync(client, cookies, tenantId, agent.Id, limit: 2, cursor: firstPage.NextCursor);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(d => d.Id).Intersect(secondPage.Items.Select(d => d.Id)));
    }

    [Fact]
    public async Task Indexing_records_what_the_embedding_calls_consumed()
    {
        // orbita-schema.dbml: measurement from day one, because it cannot be
        // reconstructed later.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/knowledge/text",
            cookies,
            new { title = "Horarios", text = "Abrimos de 7am a 7pm." });

        await RunIndexerAsync();

        using var scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var runs = await unitOfWork.QueryInTenantScopeAsync(
            _ => Task.FromResult(dbContext.AiRuns.Where(r => r.TenantId == tenantId).ToList()),
            CancellationToken.None);

        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.Equal(agent.Id, run.AgentId));
        Assert.All(runs, run => Assert.True(run.TokensIn > 0));
    }

    private async Task RunIndexerAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IKnowledgeIndexer>().IndexPendingAsync(CancellationToken.None);
    }

    private async Task<int> CountChunksAsync(Guid tenantId, Guid documentId)
    {
        using var scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        var chunks = scope.ServiceProvider.GetRequiredService<IKnowledgeChunkRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        return await unitOfWork.QueryInTenantScopeAsync(
            ct => chunks.CountByDocumentAsync(tenantId, documentId, ct),
            CancellationToken.None);
    }

    private async Task<AiAgent> GetSeededAgentAsync(Guid tenantId)
    {
        using var scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        var agents = scope.ServiceProvider.GetRequiredService<IAiAgentRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var seeded = await unitOfWork.QueryInTenantScopeAsync(
            ct => agents.ListByTenantAsync(tenantId, ct),
            CancellationToken.None);

        return Assert.Single(seeded);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        string fileName,
        string contents)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/knowledge");

        var cookieHeader = cookies.ToHeader();
        if (cookieHeader.Length > 0)
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(contents));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        request.Content = form;

        var response = await client.SendAsync(request);
        cookies.Capture(response);
        return response;
    }

    private static async Task<CursorPage<KnowledgeDocumentDto>> ListAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        int? limit = null,
        string? cursor = null)
    {
        var query = new List<string>();

        if (limit is { } pageSize)
        {
            query.Add($"limit={pageSize}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={cursor}");
        }

        var url = $"/api/tenants/{tenantId}/ai-agents/{agentId}/knowledge"
            + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, url, cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CursorPage<KnowledgeDocumentDto>>(TestRequests.JsonOptions))!;
    }

    private static async Task<KnowledgeDocumentDto> GetDocumentAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        Guid documentId)
    {
        var page = await ListAsync(client, cookies, tenantId, agentId, limit: 100);
        return Assert.Single(page.Items, document => document.Id == documentId);
    }
}
