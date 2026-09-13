using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C10's backend, end to end against a real Postgres with Row Level Security applying.
/// This is the contract screens 2.5 and 2.6 consume, so the shape of what comes back
/// matters as much as the behaviour.
/// </summary>
public sealed class AiAgentsApiTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    [Fact]
    public async Task A_new_organization_already_has_one_assistant_to_configure()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Panadería Aurora");

        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        Assert.False(agent.IsEnabled);
        Assert.Equal(FormalityLevel.Balanced, agent.Style.Formality);
        Assert.Equal(VerbosityLevel.Balanced, agent.Style.Verbosity);
        Assert.Equal(EnergyLevel.Balanced, agent.Style.Energy);
        Assert.Equal([AiToolCatalog.ConsultarConocimiento], agent.Tools);
        Assert.False(agent.HasUnpublishedChanges);
        Assert.Null(agent.Draft);
    }

    [Fact]
    public async Task The_payload_never_leaks_model_vocabulary()
    {
        // Screen 2.6 is forbidden from showing "prompt", "modelo", "temperatura" or
        // "tokens". The cheapest way to keep that true is to never send them.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents", cookies);
        var json = await response.Content.ReadAsStringAsync();

        foreach (var banned in new[] { "systemPrompt", "temperature", "maxTokens", "model" })
        {
            Assert.DoesNotContain(banned, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Creating_an_assistant_returns_it_with_a_location()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents",
            cookies,
            new
            {
                name = "Sofía",
                personality = "Eres cercana y directa.",
                instructions = "Nunca prometes descuentos que no estén en el catálogo.",
                style = Style("Warm", "Brief", "Enthusiastic"),
                tools = new[] { AiToolCatalog.ConsultarConocimiento },
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions);
        Assert.Equal("Sofía", created!.Name);
        Assert.Equal(FormalityLevel.Warm, created.Style.Formality);
        Assert.Equal(VerbosityLevel.Brief, created.Style.Verbosity);
        Assert.Equal(EnergyLevel.Enthusiastic, created.Style.Energy);
        // Created off, always: turning it on is a separate, deliberate act.
        Assert.False(created.IsEnabled);
        // A brand-new assistant is already published — there is nothing pending on it.
        Assert.False(created.HasUnpublishedChanges);
    }

    [Fact]
    public async Task Saving_parks_the_changes_without_touching_the_live_assistant()
    {
        // The heart of the draft/publish rule: an assistant that is answering customers
        // must not change because somebody is editing it.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        var saved = await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Asistente de ventas");

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var updated = (await saved.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;

        // Live is exactly as it was...
        Assert.Equal(agent.Name, updated.Name);
        Assert.Equal(agent.Personality, updated.Personality);
        Assert.Equal(agent.Style, updated.Style);
        // ...and the edits came back as the pending draft.
        Assert.True(updated.HasUnpublishedChanges);
        Assert.Equal("Asistente de ventas", updated.Draft!.Name);
        Assert.Equal(FormalityLevel.Formal, updated.Draft.Style.Formality);

        // And that survives a round trip to the database, not just the response.
        var reloaded = await GetAsync(client, cookies, tenantId, agent.Id);
        Assert.Equal(agent.Name, reloaded.Name);
        Assert.Equal("Asistente de ventas", reloaded.Draft!.Name);
    }

    [Fact]
    public async Task Saving_twice_replaces_the_pending_draft_rather_than_stacking_another()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Primera idea");
        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Segunda idea");

        var reloaded = await GetAsync(client, cookies, tenantId, agent.Id);
        Assert.Equal("Segunda idea", reloaded.Draft!.Name);
    }

    [Fact]
    public async Task Saving_the_form_unchanged_does_not_claim_there_are_pending_changes()
    {
        // A badge that lights up for nothing is a badge the owner learns to ignore, and
        // then they ignore a real pending change too.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/ai-agents/{agent.Id}",
            cookies,
            new
            {
                name = agent.Name,
                personality = agent.Personality,
                instructions = agent.Instructions,
                style = Style(
                    agent.Style.Formality.ToString(),
                    agent.Style.Verbosity.ToString(),
                    agent.Style.Energy.ToString()),
                tools = agent.Tools,
            });

        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;

        Assert.False(updated.HasUnpublishedChanges);
        Assert.Null(updated.Draft);
    }

    [Fact]
    public async Task Publishing_puts_the_draft_live_and_clears_it()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));
        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Asistente de ventas");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/publish", cookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = (await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;

        Assert.Equal("Asistente de ventas", published.Name);
        Assert.Equal("Eres entusiasta.", published.Personality);
        Assert.Equal(FormalityLevel.Formal, published.Style.Formality);
        Assert.Empty(published.Tools);
        Assert.False(published.HasUnpublishedChanges);
        Assert.Null(published.Draft);

        var reloaded = await GetAsync(client, cookies, tenantId, agent.Id);
        Assert.Equal("Asistente de ventas", reloaded.Name);
        Assert.Null(reloaded.Draft);
    }

    [Fact]
    public async Task Publishing_does_not_turn_the_assistant_on()
    {
        // Two separate decisions: what the assistant says, and whether it says it to
        // customers. Conflating them means saving a configuration lets it loose.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));
        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Asistente de ventas");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/publish", cookies);
        response.EnsureSuccessStatusCode();

        Assert.False((await GetAsync(client, cookies, tenantId, agent.Id)).IsEnabled);
    }

    [Fact]
    public async Task Publishing_with_nothing_pending_is_a_conflict()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/publish", cookies);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Discarding_throws_the_draft_away_and_leaves_live_alone()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));
        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Asistente de ventas");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/draft", cookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await GetAsync(client, cookies, tenantId, agent.Id);
        Assert.Equal(agent.Name, reloaded.Name);
        Assert.False(reloaded.HasUnpublishedChanges);
        Assert.Null(reloaded.Draft);
    }

    [Fact]
    public async Task The_on_off_switch_works_on_its_own()
    {
        // Screen 2.5 flips it from the list, without the rest of the agent loaded.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/enabled",
            cookies,
            new { isEnabled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!.IsEnabled);
        Assert.True((await GetAsync(client, cookies, tenantId, agent.Id)).IsEnabled);
    }

    [Fact]
    public async Task Turning_an_assistant_on_does_not_publish_what_is_pending()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));
        await SaveDraftAsync(client, cookies, tenantId, agent.Id, "Asistente de ventas");

        await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/ai-agents/{agent.Id}/enabled",
            cookies,
            new { isEnabled = true });

        var reloaded = await GetAsync(client, cookies, tenantId, agent.Id);
        Assert.True(reloaded.IsEnabled);
        Assert.Equal(agent.Name, reloaded.Name);
        Assert.True(reloaded.HasUnpublishedChanges);
    }

    [Fact]
    public async Task A_tool_that_does_not_work_yet_is_refused()
    {
        // Better a 400 now than an assistant that looks configured and silently never
        // creates an opportunity.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents",
            cookies,
            new
            {
                name = "Vendedor",
                personality = "Eres persuasivo.",
                instructions = "Cierras ventas.",
                style = Style("Balanced", "Balanced", "Balanced"),
                tools = new[] { AiToolCatalog.CrearOportunidad },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_draft_that_could_never_be_published_is_refused_when_it_is_saved()
    {
        // Validating only at publish time would let someone park an unpublishable draft
        // and find out days later, with no idea which edit broke it.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/ai-agents/{agent.Id}",
            cookies,
            new
            {
                name = "Vendedor",
                personality = "Eres persuasivo.",
                instructions = "Cierras ventas.",
                style = Style("Balanced", "Balanced", "Balanced"),
                tools = new[] { AiToolCatalog.CrearOportunidad },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await GetAsync(client, cookies, tenantId, agent.Id)).HasUnpublishedChanges);
    }

    [Fact]
    public async Task The_last_assistant_cannot_be_deleted()
    {
        // Same guarantee ORB-A08 gives for the last Owner: screen 2.5 has no "create your
        // first" path, so deleting the only one would strand the owner.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agent = Assert.Single(await ListAsync(client, cookies, tenantId));

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-agents/{agent.Id}", cookies);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await ListAsync(client, cookies, tenantId));
    }

    [Fact]
    public async Task A_second_assistant_can_be_deleted()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var created = await CreateAsync(client, cookies, tenantId, "Temporal");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-agents/{created.Id}", cookies);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await ListAsync(client, cookies, tenantId));
    }

    [Fact]
    public async Task A_pending_draft_goes_with_the_assistant_when_it_is_deleted()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var created = await CreateAsync(client, cookies, tenantId, "Temporal");
        await SaveDraftAsync(client, cookies, tenantId, created.Id, "Otro nombre");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-agents/{created.Id}", cookies);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await ListAsync(client, cookies, tenantId));
    }

    [Fact]
    public async Task Assistants_never_cross_tenants()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = Assert.Single(await ListAsync(client, cookiesA, tenantA));

        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        // Tenant A's agent id, asked for through tenant B's own route.
        var read = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantB}/ai-agents/{agentA.Id}", cookiesB);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var write = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantB}/ai-agents/{agentA.Id}/enabled",
            cookiesB,
            new { isEnabled = true });
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);

        var publish = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantB}/ai-agents/{agentA.Id}/publish", cookiesB);
        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);

        // And A's assistant is untouched.
        Assert.False((await GetAsync(client, cookiesA, tenantA, agentA.Id)).IsEnabled);
    }

    [Fact]
    public async Task Drafts_never_cross_tenants()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentA = Assert.Single(await ListAsync(client, cookiesA, tenantA));
        await SaveDraftAsync(client, cookiesA, tenantA, agentA.Id, "Secreto de A");

        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var read = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantB}/ai-agents/{agentA.Id}", cookiesB);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        // And B's own agent has no draft of its own leaking in.
        var agentB = Assert.Single(await ListAsync(client, cookiesB, tenantB));
        Assert.Null(agentB.Draft);
    }

    [Fact]
    public async Task A_caller_outside_the_tenant_is_refused()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, _) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, _, _, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantA}/ai-agents", cookiesB);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_tool_catalog_marks_what_does_not_work_yet_and_says_why()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, _, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, "/api/ai-tools", cookies);
        response.EnsureSuccessStatusCode();

        var tools = await response.Content.ReadFromJsonAsync<List<AiTool>>(TestRequests.JsonOptions);

        Assert.Equal(5, tools!.Count);
        Assert.True(tools.Single(tool => tool.Key == AiToolCatalog.ConsultarConocimiento).IsAvailable);

        var pending = tools.Single(tool => tool.Key == AiToolCatalog.CrearOportunidad);
        Assert.False(pending.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(pending.UnavailableReason));
    }

    [Fact]
    public async Task An_assistants_knowledge_goes_with_it_when_it_is_deleted()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var created = await CreateAsync(client, cookies, tenantId, "Temporal");

        await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{created.Id}/knowledge/text",
            cookies,
            new { title = "Nota", text = "Contenido de prueba." });

        var deleted = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-agents/{created.Id}", cookies);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The documents are gone with the agent: knowledge_docs.agent_id cascades.
        var orphaned = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{created.Id}/knowledge", cookies);
        var page = await orphaned.Content.ReadFromJsonAsync<Application.Common.CursorPage<KnowledgeDocumentDto>>(
            TestRequests.JsonOptions);

        Assert.Empty(page!.Items);
    }

    /// <summary>The three sliders, as the screen sends them.</summary>
    private static object Style(string formality, string verbosity, string energy)
        => new { formality, verbosity, energy };

    /// <summary>One "Guardar" with a known set of edits, so the assertions can name them.</summary>
    private static Task<HttpResponseMessage> SaveDraftAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        Guid agentId,
        string name)
        => TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}",
            cookies,
            new
            {
                name,
                personality = "Eres entusiasta.",
                instructions = "Ofreces el catálogo completo.",
                style = Style("Formal", "Detailed", "Neutral"),
                tools = Array.Empty<string>(),
            });

    private static async Task<AiAgentDto> CreateAsync(HttpClient client, CookieJar cookies, Guid tenantId, string name)
    {
        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents",
            cookies,
            new
            {
                name,
                personality = "Eres servicial.",
                instructions = "Respondes con los documentos del negocio.",
                style = Style("Balanced", "Balanced", "Balanced"),
                tools = Array.Empty<string>(),
            });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;
    }

    private static async Task<IReadOnlyList<AiAgentDto>> ListAsync(HttpClient client, CookieJar cookies, Guid tenantId)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents", cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<AiAgentDto>>(TestRequests.JsonOptions))!;
    }

    private static async Task<AiAgentDto> GetAsync(HttpClient client, CookieJar cookies, Guid tenantId, Guid agentId)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}", cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;
    }
}
