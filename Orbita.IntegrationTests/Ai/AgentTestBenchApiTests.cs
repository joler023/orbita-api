using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C11's test bench end to end: real Postgres, real Row Level Security, real retrieval
/// over pgvector. Only the model is faked, and only so the assertions are about what was
/// asked rather than about what some model happened to answer.
/// </summary>
public sealed class AgentTestBenchApiTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    [Fact]
    public async Task The_assistant_answers_and_says_what_it_cost()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        fixture.Llm.Reset();
        fixture.Llm.NextReply = "Sí, hacemos envíos a toda la ciudad.";

        var result = await RunAsync(client, cookies, tenantId, agent.Id, "¿Hacen envíos?");

        Assert.Equal("Sí, hacemos envíos a toda la ciudad.", result.Reply);
        Assert.False(result.TestedDraft);
        // Screen 2.8 is a diagnostic screen, so unlike 2.5 and 2.6 it may show this.
        Assert.Equal(10, result.Usage.TokensIn);
        Assert.Equal(5, result.Usage.TokensOut);
    }

    [Fact]
    public async Task The_answer_is_grounded_in_the_assistants_own_documents()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        const string note = "El pan artesanal cuesta 5000 pesos.";
        await AddNoteAsync(client, cookies, tenantId, agent.Id, "Precios", note);
        await IndexAllAsync();

        fixture.Llm.Reset();
        // The fake embedder is a hash, so the question has to be the chunk's own text for
        // it to rank first — which is what makes this a real retrieval assertion.
        var result = await RunAsync(client, cookies, tenantId, agent.Id, note);

        var hit = Assert.Single(result.Retrieved);
        Assert.Equal("Precios", hit.DocumentTitle);
        Assert.InRange(hit.Score, 0.99, 1.0001);

        // And it was actually sent, not just displayed — a screen that shows a provenance
        // the answer never had is worse than one that shows none.
        var prompt = string.Join("\n", fixture.Llm.LastCompletionRequest!.Messages.Select(m => m.Content));
        Assert.Contains(note, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_knowledge_lookup_is_reported_as_a_tool_that_ran()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        fixture.Llm.Reset();
        var result = await RunAsync(client, cookies, tenantId, agent.Id, "¿Qué venden?");

        var trace = Assert.Single(result.ToolCalls);
        Assert.Equal(AiToolCatalog.ConsultarConocimiento, trace.Tool);
        Assert.False(string.IsNullOrWhiteSpace(trace.Summary));
    }

    [Fact]
    public async Task Unpublished_changes_are_what_gets_tested()
    {
        // The whole reason drafts exist: try it before customers get it.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Sofía", "Eres entusiasta y cercana.");

        fixture.Llm.Reset();
        var result = await RunAsync(client, cookies, tenantId, agent.Id, "Hola");

        Assert.True(result.TestedDraft);

        var systemPrompt = fixture.Llm.LastCompletionRequest!.Messages
            .First(m => m.Role == LlmMessageRole.System).Content;
        Assert.Contains("Sofía", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("Eres entusiasta y cercana.", systemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Testing_a_draft_neither_publishes_nor_discards_it()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Sofía", "Eres entusiasta y cercana.");

        fixture.Llm.Reset();
        await RunAsync(client, cookies, tenantId, agent.Id, "Hola");

        var reloaded = await GetAgentAsync(client, cookies, tenantId, agent.Id);
        Assert.Equal("Asistente", reloaded.Name);
        Assert.True(reloaded.HasUnpublishedChanges);
        Assert.Equal("Sofía", reloaded.Draft!.Name);
    }

    [Fact]
    public async Task What_the_test_cost_lands_in_the_run_log()
    {
        // ORB-C01's rule holds here too: a model call nobody measured cannot be measured
        // afterwards. The test bench spends real money once a real provider is connected.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        var before = await CountRunsAsync(tenantId);

        fixture.Llm.Reset();
        await RunAsync(client, cookies, tenantId, agent.Id, "¿Qué venden?");

        // Two: the question's embedding and the reply.
        Assert.Equal(before + 2, await CountRunsAsync(tenantId));
    }

    [Fact]
    public async Task An_empty_message_is_refused()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        var response = await SendAsync(client, cookies, tenantId, agent.Id, new { message = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_provider_that_is_down_is_a_bad_gateway_not_a_server_error()
    {
        // The request was fine and this API is fine. A 500 would send the owner looking for
        // a bug in their own configuration.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        fixture.Llm.Reset();
        fixture.Llm.NextFailure = new LlmProviderException("fake", "no responde", isTransient: true);

        var response = await SendAsync(client, cookies, tenantId, agent.Id, new { message = "Hola" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        fixture.Llm.Reset();
    }

    [Fact]
    public async Task An_assistant_from_another_tenant_cannot_be_tested()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = await SeededAgentAsync(tenantA);

        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        // Tenant A's assistant, asked for through tenant B's own route.
        var response = await SendAsync(client, cookiesB, tenantB, agentA.Id, new { message = "Hola" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_caller_outside_the_tenant_is_refused()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, _) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = await SeededAgentAsync(tenantA);
        var (_, _, _, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var response = await SendAsync(client, cookiesB, tenantA, agentA.Id, new { message = "Hola" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Another_tenants_documents_never_reach_the_prompt()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = await SeededAgentAsync(tenantA);

        const string secret = "Nuestro margen de ganancia es del 40 por ciento.";
        await AddNoteAsync(client, cookiesA, tenantA, agentA.Id, "Interno", secret);
        await IndexAllAsync();

        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");
        var agentB = await SeededAgentAsync(tenantB);

        fixture.Llm.Reset();
        // B asks the exact question that would match A's note.
        var result = await RunAsync(client, cookiesB, tenantB, agentB.Id, secret);

        Assert.Empty(result.Retrieved);

        // The text is in the prompt — B typed it — but only as B's own question. What must
        // never happen is it arriving as a document B is told to answer from, so the
        // assertion is on the grounding turns rather than on the whole prompt.
        var grounding = fixture.Llm.LastCompletionRequest!.Messages
            .Where(m => m.Role == LlmMessageRole.System)
            .Select(m => m.Content);

        Assert.DoesNotContain(grounding, content => content.Contains("40 por ciento", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_exchange_the_caller_sends_is_what_continues_the_conversation()
    {
        // Nothing is stored between calls — the transcript is the caller's, which is why
        // this needs no conversations table and is not blocked on Track B.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = await SeededAgentAsync(tenantId);

        fixture.Llm.Reset();
        await SendAsync(
            client,
            cookies,
            tenantId,
            agent.Id,
            new
            {
                message = "¿Y a Cali?",
                history = new[]
                {
                    new { role = "User", content = "¿Hacen envíos?" },
                    new { role = "Assistant", content = "Sí, a todo el país." },
                },
            });

        var conversation = fixture.Llm.LastCompletionRequest!.Messages
            .Where(m => m.Role != LlmMessageRole.System)
            .ToList();

        Assert.Equal(3, conversation.Count);
        Assert.Equal("¿Hacen envíos?", conversation[0].Content);
        Assert.Equal(LlmMessageRole.Assistant, conversation[1].Role);
        Assert.Equal("¿Y a Cali?", conversation[2].Content);
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        object body)
        => TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-chat", cookies, body);

    private static async Task<AgentTestResult> RunAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        string message)
    {
        var response = await SendAsync(client, cookies, tenantId, agentId, new { message });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AgentTestResult>(TestRequests.JsonOptions))!;
    }

    private static Task AddNoteAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        string title,
        string text)
        => TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/knowledge/text",
            cookies,
            new { title, text });

    private static async Task SaveDraftAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        string name,
        string personality)
    {
        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}",
            cookies,
            new
            {
                name,
                personality,
                instructions = "Ofreces el catálogo completo.",
                style = new { formality = "Warm", verbosity = "Detailed", energy = "Enthusiastic" },
                tools = new[] { AiToolCatalog.ConsultarConocimiento },
            });

        response.EnsureSuccessStatusCode();
    }

    private static async Task<AiAgentDto> GetAgentAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}", cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;
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

    private async Task<AiAgent> SeededAgentAsync(Guid tenantId)
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

    private async Task<int> CountRunsAsync(Guid tenantId)
    {
        using var scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        return await unitOfWork.QueryInTenantScopeAsync(
            ct => dbContext.AiRuns.CountAsync(run => run.TenantId == tenantId, ct),
            CancellationToken.None);
    }
}
