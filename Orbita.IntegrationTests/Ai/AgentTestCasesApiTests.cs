using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C11's saved cases over HTTP, plus the journey screen 2.6 actually walks — configure
/// the assistant, then read it back and find what was configured. The frontend builds
/// against mocks, so the only thing that catches a serialization drift is a test that goes
/// through the real pipeline.
/// </summary>
public sealed class AgentTestCasesApiTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public AgentTestCasesApiTests(TenantsApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_saved_case_comes_back_with_its_turns_in_order()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentId = await FirstAgentAsync(client, tenantId, cookies);

        var created = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-cases", cookies,
            new
            {
                name = "Domicilios a Belén",
                messages = new[]
                {
                    new { role = "User", content = "¿hacen domicilios?" },
                    new { role = "Assistant", content = "Sí, en Laureles y Belén." },
                    new { role = "User", content = "¿cuánto cuesta?" },
                },
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-cases", cookies);
        var cases = await list.Content.ReadFromJsonAsync<List<AgentTestCaseDto>>(TestRequests.JsonOptions);

        var saved = Assert.Single(cases!);
        Assert.Equal("Domicilios a Belén", saved.Name);

        // The order of an exchange is the exchange. A jsonb array preserves it; this is the
        // test that would fail if the mapping ever stopped doing so.
        Assert.Equal(3, saved.Messages.Count);
        Assert.Equal(AgentTestRole.User, saved.Messages[0].Role);
        Assert.Equal("Sí, en Laureles y Belén.", saved.Messages[1].Content);
        Assert.Equal("¿cuánto cuesta?", saved.Messages[2].Content);

        var deleted = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-cases/{saved.Id}", cookies);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var after = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-cases", cookies);
        Assert.Empty((await after.Content.ReadFromJsonAsync<List<AgentTestCaseDto>>(TestRequests.JsonOptions))!);
    }

    [Fact]
    public async Task One_tenants_saved_cases_are_invisible_to_another()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Panadería Uno");
        var agentId = await FirstAgentAsync(client, tenantId, cookies);
        var (_, _, _, outsider) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Panadería Dos");

        var saved = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-cases", cookies,
            new { name = "Interno", messages = new[] { new { role = "User", content = "¿precio mayorista?" } } });
        saved.EnsureSuccessStatusCode();

        var read = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-cases", outsider);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task A_case_with_no_messages_is_refused()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentId = await FirstAgentAsync(client, tenantId, cookies);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/test-cases", cookies,
            new { name = "Vacío", messages = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_configuration_journey_reads_back_everything_it_wrote()
    {
        // Exactly the order screen 2.6 walks: create, set limits, set a split shift, read
        // the assistant, clear the schedule, read again. Written because the frontend tests
        // this against mocks and nothing here proved the two agree.
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var created = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents", cookies,
            new
            {
                name = "Espiga",
                personality = "Cercano y resolutivo.",
                instructions = "Confirma la hora de recogida.",
                style = new { formality = "Balanced", verbosity = "Balanced", energy = "Balanced" },
                tools = new[] { "consultar_conocimiento" },
            });
        created.EnsureSuccessStatusCode();
        var agentId = (await created.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!.Id;

        var guardrails = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/guardrails", cookies,
            new { blockedTopics = new[] { "dosis", "diagnóstico" }, outOfScopeReply = "Eso lo ve el equipo." });
        guardrails.EnsureSuccessStatusCode();

        var hours = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/business-hours", cookies,
            new
            {
                businessHours = new
                {
                    slots = new[]
                    {
                        new { day = "Monday", opens = "08:00:00", closes = "12:00:00" },
                        new { day = "Monday", opens = "14:00:00", closes = "18:00:00" },
                    },
                    outsideHours = "LeaveForTeam",
                },
            });
        hours.EnsureSuccessStatusCode();

        var agent = await GetAgentAsync(client, tenantId, agentId, cookies);

        Assert.Equal(["dosis", "diagnóstico"], agent.Guardrails.BlockedTopics);
        Assert.Equal("Eso lo ve el equipo.", agent.Guardrails.OutOfScopeReply);

        // Both halves of the split shift come back, in the order they were sent — the
        // frontend's editor keeps several slots per day precisely so saving never drops one
        // silently, and that only works if the order survives the round trip.
        Assert.Equal(2, agent.BusinessHours!.Slots.Count);
        Assert.Equal(new TimeOnly(8, 0), agent.BusinessHours.Slots[0].Opens);
        Assert.Equal(new TimeOnly(12, 0), agent.BusinessHours.Slots[0].Closes);
        Assert.Equal(new TimeOnly(14, 0), agent.BusinessHours.Slots[1].Opens);
        Assert.Equal(new TimeOnly(18, 0), agent.BusinessHours.Slots[1].Closes);
        Assert.Equal(OutsideHoursBehavior.LeaveForTeam, agent.BusinessHours.OutsideHours);

        var cleared = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/business-hours", cookies,
            new { businessHours = (object?)null });
        cleared.EnsureSuccessStatusCode();

        var afterClear = await GetAgentAsync(client, tenantId, agentId, cookies);
        Assert.Null(afterClear.BusinessHours);

        // Clearing the schedule must not have touched the limits, which are a different
        // subresource with a different lifetime.
        Assert.Equal(["dosis", "diagnóstico"], afterClear.Guardrails.BlockedTopics);
    }

    private static async Task<AiAgentDto> GetAgentAsync(HttpClient client, Guid tenantId, Guid agentId, CookieJar cookies)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}", cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;
    }

    private static async Task<Guid> FirstAgentAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var list = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents", cookies);
        list.EnsureSuccessStatusCode();

        return (await list.Content.ReadFromJsonAsync<List<AiAgentDto>>(TestRequests.JsonOptions))![0].Id;
    }
}
