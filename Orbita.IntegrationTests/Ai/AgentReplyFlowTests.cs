using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Ai;
using Orbita.Application.Channels;
using Orbita.Domain.Ai;
using Orbita.Domain.Inbox;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C04 end to end, through every real seam: a signed WhatsApp webhook lands on the
/// queue (B02), <c>InboundMessageWorker</c> turns it into a message (B03) and stages
/// <c>message.received</c>, <c>OutboxDispatcherWorker</c> publishes it (B04), and the
/// assistant's reply comes back out queued on <c>outbound_message_jobs</c> (B05).
///
/// Written against the database rather than an endpoint because C04 has no endpoint —
/// nobody asks for this reply, it happens because a customer wrote.
/// </summary>
public sealed class AgentReplyFlowTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public AgentReplyFlowTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Llm.Reset();
    }

    [Fact]
    public async Task An_inbound_message_gets_an_assistant_reply_queued_for_sending()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);

        _fixture.Llm.NextReply = "Claro que sí, abrimos hasta las 7 p.m.";
        var externalId = $"wamid.{Guid.NewGuid():N}";

        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573001234567", externalId, "¿hasta qué hora abren?"));

        var reply = await WaitForAgentReplyAsync(externalId);

        Assert.Equal("Claro que sí, abrimos hasta las 7 p.m.", reply.Body);
        Assert.Equal(MessageDirection.Outbound, reply.Direction);

        // The two halves of "an assistant wrote this": nobody to attribute it to, and a
        // run that says what it cost.
        Assert.Null(reply.SentByUserId);
        Assert.NotNull(reply.AiRunId);

        await using var db = _fixture.CreateOwnerDbContext();

        var conversation = await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == reply.ConversationId);
        Assert.Equal(agentId, conversation.AiAgentId);

        var run = await db.AiRuns.AsNoTracking().SingleAsync(r => r.Id == reply.AiRunId);
        Assert.Equal(agentId, run.AgentId);
        Assert.Equal(conversation.Id, run.ConversationId);

        // Persist-first, same as any human's message: the reply is queued for the
        // dispatcher, not sent inline.
        Assert.True(await db.OutboundMessageJobs.AnyAsync(j => j.MessageId == reply.Id));
    }

    [Fact]
    public async Task A_blocked_topic_is_answered_with_the_owners_sentence_immediately()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);

        // Through the subresource, not the draft: saving must take effect without Publicar.
        var guardrails = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/guardrails", cookies,
            new { blockedTopics = new[] { "dosis" }, outOfScopeReply = "Eso lo ve el equipo médico, escríbeles directamente." });
        guardrails.EnsureSuccessStatusCode();

        var dto = (await guardrails.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;
        Assert.Equal(["dosis"], dto.Guardrails.BlockedTopics);
        Assert.False(dto.HasUnpublishedChanges);

        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573001112299", externalId, "¿qué dosis tomo?"));

        var reply = await WaitForAgentReplyAsync(externalId);

        Assert.Equal("Eso lo ve el equipo médico, escríbeles directamente.", reply.Body);

        // Neither a person nor the model wrote it.
        Assert.Equal(MessageAuthorKind.System, reply.AuthorKind);
        Assert.Null(reply.AiRunId);

        await using var owner = _fixture.CreateOwnerDbContext();
        Assert.True(await owner.Set<Orbita.Domain.Outbox.OutboxEvent>()
            .AnyAsync(e => e.EventType == "agent.reply_blocked" && e.TenantId == tenantId));
    }

    [Fact]
    public async Task An_ordinary_word_containing_a_blocked_topic_is_still_answered()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);

        (await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/guardrails", cookies,
            new { blockedTopics = new[] { "talla" }, outOfScopeReply = "No hablo de tallas." })).EnsureSuccessStatusCode();

        _fixture.Llm.NextReply = "Probá recargar la página, suele arreglarlo.";
        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573001112288", externalId, "no me carga la pantalla"));

        var reply = await WaitForAgentReplyAsync(externalId);

        // "pantalla" contains "talla". Substring matching would have silenced this.
        Assert.Equal(MessageAuthorKind.AiAgent, reply.AuthorKind);
    }

    [Fact]
    public async Task The_assistant_registers_an_opportunity_for_the_customer_it_is_talking_to()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);
        await EnableToolsAsync(client, tenantId, cookies, agentId, AiToolCatalog.CrearOportunidad);

        _fixture.Llm.NextToolCalls.Add(new LlmToolCall("call_1", AiToolCatalog.CrearOportunidad, """{"titulo":"Torta para 30 personas","monto":180000}"""));
        _fixture.Llm.NextReply = "Listo, dejé registrado tu pedido.";

        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573001234000", externalId, "quiero una torta para 30 personas"));

        var reply = await WaitForAgentReplyAsync(externalId);
        Assert.Equal("Listo, dejé registrado tu pedido.", reply.Body);

        await using var owner = _fixture.CreateOwnerDbContext();
        var conversation = await owner.Conversations.AsNoTracking().SingleAsync(c => c.Id == reply.ConversationId);

        // On the right contact, and audited as the assistant — criteria of the story.
        var opportunity = await owner.Opportunities.AsNoTracking().SingleAsync(o => o.ContactId == conversation.ContactId);
        Assert.Equal("Torta para 30 personas", opportunity.Title);
        Assert.Equal(180000m, opportunity.Amount);

        Assert.True(await owner.AuditLogEntries.AsNoTracking().AnyAsync(e =>
            e.EntityId == opportunity.Id
            && e.Action == "opportunity.created"
            && e.ActorType == Orbita.Domain.Audit.AuditActorType.AiAgent));
    }

    [Fact]
    public async Task An_assistant_cannot_create_anything_in_another_tenant()
    {
        var client = TestRequests.CreateClient(_fixture);

        var (_, _, victimTenant, victimCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Víctima");
        await using (var before = _fixture.CreateOwnerDbContext())
        {
            // Positive control for the negative below: the victim's board is readable here.
            Assert.True(await before.Pipelines.AsNoTracking().AnyAsync(p => p.TenantId == victimTenant));
        }

        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Atacante");
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);
        await EnableToolsAsync(client, tenantId, cookies, agentId, AiToolCatalog.CrearOportunidad);

        // The model names the other tenant outright. The executor never reads it.
        _fixture.Llm.NextToolCalls.Add(new LlmToolCall(
            "call_1",
            AiToolCatalog.CrearOportunidad,
            $$"""{"titulo":"Intruso","tenantId":"{{victimTenant}}"}"""));
        _fixture.Llm.NextReply = "Hecho.";

        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573001234111", externalId, "hola"));
        await WaitForAgentReplyAsync(externalId);

        await using var owner = _fixture.CreateOwnerDbContext();
        Assert.False(await owner.Opportunities.AsNoTracking().AnyAsync(o => o.TenantId == victimTenant));
        Assert.True(await owner.Opportunities.AsNoTracking().AnyAsync(o => o.TenantId == tenantId && o.Title == "Intruso"));
    }

    [Fact]
    public async Task A_disabled_assistant_never_answers()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);

        // The default assistant an organization is registered with is switched off
        // (ORB-C10) — this test simply never turns it on.
        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573007654321", externalId, "hola"));

        var inbound = await WaitForInboundAsync(externalId);

        // Give the dispatcher room to have published and the handler to have run, so this
        // asserts "decided not to answer" rather than "has not answered yet".
        await Task.Delay(TimeSpan.FromSeconds(3));

        await using var db = _fixture.CreateOwnerDbContext();

        Assert.False(await db.Messages.AnyAsync(m =>
            m.ConversationId == inbound.ConversationId && m.Direction == MessageDirection.Outbound));
    }

    [Fact]
    public async Task The_reply_is_grounded_in_the_assistant_own_documents()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);

        var knowledge = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/knowledge/text",
            cookies,
            new { title = "Horarios", text = "La panadería abre de 7 a.m. a 7 p.m. de lunes a sábado." });
        knowledge.EnsureSuccessStatusCode();

        // The fixture removes the background indexer on purpose, so a document is only
        // chunked when a test asks for it. Waiting instead of asking waits forever.
        await IndexAllAsync();

        await using (var owner = _fixture.CreateOwnerDbContext())
        {
            Assert.True(await owner.KnowledgeChunks.AnyAsync(c => c.TenantId == tenantId));
        }

        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(account.ExternalId, "573009990000", externalId, "¿a qué hora abren?"));

        await WaitForAgentReplyAsync(externalId);

        var prompt = _fixture.Llm.LastCompletionRequest;
        Assert.NotNull(prompt);

        var system = string.Join("\n", prompt!.Messages
            .Where(m => m.Role == LlmMessageRole.System)
            .Select(m => m.Content));

        Assert.Contains("de 7 a.m. a 7 p.m.", system, StringComparison.Ordinal);
        Assert.Contains("mismo idioma", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reply is whatever outbound message shows up on the conversation the inbound
    /// one created — found by polling, because three workers have to take their turn
    /// between the POST and the answer.
    /// </summary>
    private async Task<Message> WaitForAgentReplyAsync(string inboundExternalId)
    {
        var inbound = await WaitForInboundAsync(inboundExternalId);

        Message? reply = null;

        await Eventually.AssertAsync(async () =>
        {
            await using var db = _fixture.CreateOwnerDbContext();

            reply = await db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(m =>
                    m.ConversationId == inbound.ConversationId && m.Direction == MessageDirection.Outbound);

            return reply is not null;
        });

        return reply!;
    }

    private async Task<Message> WaitForInboundAsync(string externalId)
    {
        Message? inbound = null;

        await Eventually.AssertAsync(async () =>
        {
            await using var db = _fixture.CreateOwnerDbContext();
            inbound = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalId == externalId);

            return inbound is not null;
        });

        return inbound!;
    }

    /// <summary>
    /// Every organization is registered with one assistant, switched off. Turning it on
    /// is what a real owner does from screen 2.5, and what makes it answer at all.
    /// </summary>
    private static async Task<Guid> EnableDefaultAgentAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var list = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents", cookies);
        list.EnsureSuccessStatusCode();

        var agents = (await list.Content.ReadFromJsonAsync<List<AiAgentDto>>(TestRequests.JsonOptions))!;
        var agentId = agents[0].Id;

        var enable = await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/ai-agents/{agentId}/enabled", cookies, new { isEnabled = true });
        enable.EnsureSuccessStatusCode();

        return agentId;
    }

    private Task<ChannelAccountDto> ConnectAsync(HttpClient client, Guid tenantId, CookieJar cookies)
        // Connect *and* verify: without the handshake the account stays
        // PendingVerification and every send is refused. See the helper.
        => TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);

    /// <summary>
    /// Enables tools through the real screen path: save a draft, then publish. Tools are
    /// part of the draft (unlike guardrails), so without publishing the live assistant
    /// would still have none.
    /// </summary>
    private static async Task EnableToolsAsync(HttpClient client, Guid tenantId, CookieJar cookies, Guid agentId, params string[] tools)
    {
        var draft = await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/ai-agents/{agentId}", cookies,
            new
            {
                name = "Asistente",
                personality = "Amable y resolutivo.",
                instructions = "Registra pedidos cuando el cliente quiere comprar.",
                style = new { formality = "Balanced", verbosity = "Balanced", energy = "Balanced" },
                tools,
            });
        draft.EnsureSuccessStatusCode();

        var publish = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/publish", cookies);
        publish.EnsureSuccessStatusCode();
    }

    /// <summary>Drains the indexing queue the way the other ORB-C02/C03 tests do.</summary>
    private async Task IndexAllAsync()
    {
        for (var pass = 0; pass < 20; pass++)
        {
            using var scope = _fixture.Services.CreateScope();

            if (await scope.ServiceProvider.GetRequiredService<IKnowledgeIndexer>().IndexPendingAsync(CancellationToken.None) == 0)
            {
                return;
            }
        }
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
}
