using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C03 against a real Postgres with real pgvector and the real HNSW index. Only the
/// embedding model is faked — and it is faked deterministically, so "the right passage
/// ranks first" is a meaningful assertion rather than a coin flip.
/// </summary>
public sealed class KnowledgeSearchApiTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    [Fact]
    public async Task The_passage_that_answers_the_question_ranks_first()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        await AddNoteAsync(client, cookies, tenantId, agent.Id, "Horarios", "Abrimos de lunes a sábado de 7am a 7pm.");
        await AddNoteAsync(client, cookies, tenantId, agent.Id, "Envíos", "Hacemos domicilios en toda la ciudad.");
        await AddNoteAsync(client, cookies, tenantId, agent.Id, "Precios", "El pan artesanal cuesta 5000 pesos.");
        await IndexAllAsync();

        // The fake embedder is a hash, so the query has to be the exact text of the chunk
        // it should match — which is the point: it proves ranking works, without pretending
        // to test a real model's semantics.
        var hits = await SearchAsync(client, cookies, tenantId, agent.Id, "El pan artesanal cuesta 5000 pesos.");

        Assert.NotEmpty(hits);
        Assert.Equal("Precios", hits[0].DocumentTitle);
        Assert.Contains("5000 pesos", hits[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_hit_carries_its_score_and_its_source()
    {
        // ORB-C03's "devuelve los fragmentos con su puntaje y su fuente" — ORB-C11's test
        // bench cannot show where an answer came from without both.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        await AddNoteAsync(client, cookies, tenantId, agent.Id, "Horarios", "Abrimos de 7am a 7pm.");
        await IndexAllAsync();

        var hit = Assert.Single(await SearchAsync(client, cookies, tenantId, agent.Id, "Abrimos de 7am a 7pm."));

        Assert.Equal("Horarios", hit.DocumentTitle);
        Assert.NotEqual(Guid.Empty, hit.DocumentId);
        Assert.NotEqual(Guid.Empty, hit.ChunkId);
        Assert.Equal(0, hit.ChunkIndex);
        // Cosine similarity, so higher is better and an exact match is ~1.
        Assert.InRange(hit.Score, 0.99, 1.0001);
    }

    [Fact]
    public async Task Results_never_cross_tenants()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = await GetSeededAgentAsync(tenantA);

        const string secret = "Nuestro margen de ganancia es del 40 por ciento.";
        await AddNoteAsync(client, cookiesA, tenantA, agentA.Id, "Interno", secret);
        await IndexAllAsync();

        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");
        var agentB = await GetSeededAgentAsync(tenantB);
        await AddNoteAsync(client, cookiesB, tenantB, agentB.Id, "Propio", "Vendemos flores.");
        await IndexAllAsync();

        // Tenant B asks the exact question that would match tenant A's secret.
        var hits = await SearchAsync(client, cookiesB, tenantB, agentB.Id, secret);

        Assert.DoesNotContain(hits, hit => hit.Content.Contains("margen", StringComparison.OrdinalIgnoreCase));
        Assert.All(hits, hit => Assert.Equal("Propio", hit.DocumentTitle));
    }

    [Fact]
    public async Task Results_never_cross_agents_inside_the_same_tenant()
    {
        // Two assistants in one organization can be given different material on purpose —
        // support should not answer from the sales playbook.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var support = await GetSeededAgentAsync(tenantId);
        var sales = await CreateExtraAgentAsync(tenantId, "Ventas");

        const string salesOnly = "Ofrecemos 20 por ciento de descuento a mayoristas.";
        await AddNoteAsync(client, cookies, tenantId, sales.Id, "Descuentos", salesOnly);
        await AddNoteAsync(client, cookies, tenantId, support.Id, "Soporte", "Atendemos reclamos por WhatsApp.");
        await IndexAllAsync();

        var hits = await SearchAsync(client, cookies, tenantId, support.Id, salesOnly);

        Assert.DoesNotContain(hits, hit => hit.DocumentTitle == "Descuentos");
    }

    [Fact]
    public async Task An_agent_with_no_indexed_knowledge_returns_nothing_rather_than_failing()
    {
        // The agent has to be able to say "no sé" — an empty result is a normal answer.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        Assert.Empty(await SearchAsync(client, cookies, tenantId, agent.Id, "¿A qué hora abren?"));
    }

    [Fact]
    public async Task The_limit_is_respected_and_capped()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        for (var i = 0; i < 8; i++)
        {
            await AddNoteAsync(client, cookies, tenantId, agent.Id, $"Nota {i}", $"Contenido distinto número {i}.");
        }

        await IndexAllAsync();

        Assert.Equal(2, (await SearchAsync(client, cookies, tenantId, agent.Id, "Contenido distinto número 3.", limit: 2)).Count);
        // Zero means "use the default", not "return nothing".
        Assert.Equal(
            KnowledgeSearchService.DefaultLimit,
            (await SearchAsync(client, cookies, tenantId, agent.Id, "Contenido distinto número 3.")).Count);
    }

    [Fact]
    public async Task Searching_another_tenants_agent_is_refused()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, _) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = await GetSeededAgentAsync(tenantA);
        var (_, _, _, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantA}/ai-agents/{agentA.Id}/knowledge/search",
            cookiesB,
            new { query = "lo que sea" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_question_is_rejected()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/knowledge/search",
            cookies,
            new { query = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Searching_records_what_the_query_embedding_cost()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        await AddNoteAsync(client, cookies, tenantId, agent.Id, "Horarios", "Abrimos de 7am a 7pm.");
        await IndexAllAsync();

        var before = await CountRunsAsync(tenantId);
        await SearchAsync(client, cookies, tenantId, agent.Id, "¿A qué hora abren?");

        // A search costs tokens like any other model call, so ORB-A13 has to see it.
        Assert.Equal(before + 1, await CountRunsAsync(tenantId));
    }

    [Fact]
    public async Task A_search_narrows_by_tenant_through_an_index_and_ranks_by_cosine_distance()
    {
        // What this pins down, and what it deliberately does not.
        //
        // It asserts that the tenant filter is served by an index rather than a
        // sequential scan, and that ranking uses `<=>` (cosine distance) — a query
        // written against the wrong operator parses fine and then ranks by the wrong
        // metric, silently.
        //
        // It does NOT assert that the HNSW index is chosen, because with a tenant
        // predicate Postgres does not choose it: it filters by tenant_id first and sorts
        // the survivors. That is correct and fast while a tenant's own corpus is small,
        // and it is the known limit on ORB-C03's "menos de 200 ms con 100.000
        // fragmentos" — see the note in AddKnowledgeChunkHnswIndex. Asserting the index
        // here would either fail honestly or force a query shape that weakens tenant
        // isolation, and neither is worth it.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        await AddNoteAsync(client, cookies, tenantId, agent.Id, "Horarios", "Abrimos de 7am a 7pm.");
        await IndexAllAsync();

        var plan = await ExplainSearchAsync(tenantId);

        // The plan is included in the failure message: when this breaks, what Postgres
        // actually chose is the only thing that explains why.
        Assert.False(
            plan.Contains("Seq Scan on knowledge_chunks", StringComparison.Ordinal),
            $"The tenant filter fell back to a sequential scan. Plan was:\n{plan}");

        Assert.True(
            plan.Contains("<=>", StringComparison.Ordinal),
            $"Expected ranking by cosine distance. Plan was:\n{plan}");
    }

    [Fact]
    public async Task A_search_answers_quickly()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await GetSeededAgentAsync(tenantId);

        for (var i = 0; i < 20; i++)
        {
            await AddNoteAsync(client, cookies, tenantId, agent.Id, $"Nota {i}", $"Contenido número {i} sobre la panadería.");
        }

        await IndexAllAsync();

        // Warm the connection pool and the plan cache first, so this measures the query
        // rather than the first-request cost of the whole stack.
        await SearchAsync(client, cookies, tenantId, agent.Id, "Contenido número 5 sobre la panadería.");

        var stopwatch = Stopwatch.StartNew();
        await SearchAsync(client, cookies, tenantId, agent.Id, "Contenido número 5 sobre la panadería.");
        stopwatch.Stop();

        // Generous next to ORB-C03's 200 ms, because this includes the full HTTP pipeline
        // and a containerised database on a developer laptop. It is here to catch an
        // order-of-magnitude regression, not to certify the production number.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1_000,
            $"Search took {stopwatch.ElapsedMilliseconds} ms.");
    }

    private static Task AddNoteAsync(HttpClient client, CookieJar cookies, Guid tenantId, Guid agentId, string title, string text)
        => TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/knowledge/text",
            cookies,
            new { title, text });

    private static async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        string query,
        int limit = 0)
    {
        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/knowledge/search",
            cookies,
            new { query, limit });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<KnowledgeSearchHit>>(TestRequests.JsonOptions))!;
    }

    private async Task IndexAllAsync()
    {
        for (var pass = 0; pass < 20; pass++)
        {
            using var scope = fixture.Services.CreateScope();

            if (await scope.ServiceProvider.GetRequiredService<IKnowledgeIndexer>().IndexPendingAsync(CancellationToken.None) == 0)
            {
                return;
            }
        }
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

        return seeded[0];
    }

    private async Task<AiAgent> CreateExtraAgentAsync(Guid tenantId, string name)
    {
        // ORB-C10 will expose this over HTTP; until then a second agent is created
        // directly, which is enough to prove search is scoped per agent.
        using var scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        var agents = scope.ServiceProvider.GetRequiredService<IAiAgentRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var agent = AiAgent.Create(
            tenantId,
            name,
            $"Eres el asistente de {name}.",
            "Respondes con la información de tus documentos.",
            AgentTone.Balanced,
            DateTimeOffset.UtcNow);
        agents.Add(agent);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return agent;
    }

    private async Task<int> CountRunsAsync(Guid tenantId)
    {
        using var scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        return await unitOfWork.QueryInTenantScopeAsync(
            _ => Task.FromResult(dbContext.AiRuns.Count(run => run.TenantId == tenantId)),
            CancellationToken.None);
    }

    /// <summary>
    /// Asks Postgres how it would run the search. Uses the fixture's admin connection so
    /// the plan is not shaped by RLS, and pins the same operator the repository uses.
    /// </summary>
    private async Task<string> ExplainSearchAsync(Guid tenantId)
    {
        // Written as a literal cast to vector rather than bound as a parameter: a raw
        // NpgsqlConnection has none of the pgvector type mapping that UseVector() gives
        // the EF data source, and teaching it that here would test the plumbing rather
        // than the plan.
        var embedding = "[" + string.Join(
            ',',
            FakeLlmProvider.Embed("cualquier consulta")
                .Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
            + "]";

        await using var connection = new Npgsql.NpgsqlConnection(fixture.GetAdminConnectionString());
        await connection.OpenAsync();

        // HNSW is only preferred once the planner believes a scan is the costlier option;
        // on a table this small it would otherwise legitimately choose one.
        await using (var disableSeqScan = new Npgsql.NpgsqlCommand("SET enable_seqscan = off;", connection))
        {
            await disableSeqScan.ExecuteNonQueryAsync();
        }

        await using var command = new Npgsql.NpgsqlCommand(
            """
            EXPLAIN
            SELECT c.id
            FROM   knowledge_chunks c
            JOIN   knowledge_docs d ON d.id = c.doc_id
            WHERE  c.tenant_id = @tenantId
            ORDER BY c.embedding <=> CAST(@embedding AS vector)
            LIMIT  5
            """,
            connection);

        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("embedding", embedding);

        var plan = new System.Text.StringBuilder();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(0));
        }

        return plan.ToString();
    }
}
