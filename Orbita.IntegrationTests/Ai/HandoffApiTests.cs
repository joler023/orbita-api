using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Orbita.Application.Ai;
using Orbita.Application.Inbox;
using Orbita.Domain.Ai;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C07 over HTTP, which is the half unit tests cannot see: a rule that a business
/// owner meets through a status code and a title needs a test that pins the status code
/// and the title. Three times in this epic a 409 turned out to be a 500 in production
/// while the unit test asserting the exception stayed green (see CLAUDE.md).
///
/// The handoffs themselves are made the way a real one is — a signed webhook, the inbound
/// worker, the outbox, the assistant — rather than by writing rows, so what is asserted is
/// the product and not a fixture.
/// </summary>
public sealed class HandoffApiTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public HandoffApiTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Llm.NextReply = "El cliente pidió hablar con una persona.";
    }

    [Fact]
    public async Task A_customer_who_asks_for_a_person_shows_up_in_the_queue()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);
        await EnableDefaultAgentAsync(client, tenantId, cookies);

        _fixture.Llm.NextReply = "El cliente pidió hablar con una persona.";

        // Positive control: the queue is readable and empty, so the assertion below is
        // about a conversation arriving and not about the endpoint answering nothing.
        var before = await QueueAsync(client, tenantId, cookies);
        Assert.Empty(before.Items);
        Assert.Equal(0, before.Total);
        Assert.Null(before.NextCursor);

        await SendInboundAsync(client, account.ExternalId, "573001110001", "quiero hablar con una persona");

        var queue = await WaitForQueueAsync(client, tenantId, cookies, expected: 1);
        var waiting = Assert.Single(queue.Items);

        Assert.Equal(HandoffReason.CustomerAsked, waiting.Reason);
        Assert.Equal(1, queue.Total);

        // The name is joined server-side: the dashboard has no contacts API to resolve it
        // with, and a queue of UUIDs cannot be read by the person it is for.
        Assert.False(string.IsNullOrWhiteSpace(waiting.ContactName));

        // The note is what makes the queue worth reading, and it is the model's. It arrives
        // a moment after the handoff, written in reaction to the event, so the customer
        // never waits on it — which means a test has to wait for it instead.
        await Eventually.AssertAsync(async () =>
            (await QueueAsync(client, tenantId, cookies)).Items.SingleOrDefault()?.Summary
                == "El cliente pidió hablar con una persona.");

        // The sentence the customer got is the owner's, not the model's — a system
        // message, which on the wire is the pair (no sender, no run). AuthorKind itself is
        // derived in the entity and unmapped, so it cannot be queried.
        await using var db = _fixture.CreateOwnerDbContext();
        Assert.True(await db.Messages.AsNoTracking().AnyAsync(m =>
            m.ConversationId == waiting.ConversationId
            && m.Direction == MessageDirection.Outbound
            && m.SentByUserId == null
            && m.AiRunId == null
            && m.Body == Orbita.Domain.Ai.AiAgent.DefaultHandoffReply));

        // orbita-schema.dbml's was_handoff, unsettable until this story existed.
        Assert.True(await db.AiRuns.AsNoTracking().AnyAsync(r => r.ConversationId == waiting.ConversationId && r.WasHandoff));
    }

    [Fact]
    public async Task The_assistant_never_answers_a_conversation_it_handed_over()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);
        await EnableDefaultAgentAsync(client, tenantId, cookies);

        await SendInboundAsync(client, account.ExternalId, "573001110002", "necesito hablar con alguien");
        var queue = await WaitForQueueAsync(client, tenantId, cookies, expected: 1);
        var conversationId = queue.Items[0].ConversationId;

        var repliesAfterHandoff = await CountAgentRepliesAsync(conversationId);

        // The customer keeps writing, which is exactly what someone waiting does.
        await SendInboundAsync(client, account.ExternalId, "573001110002", "¿hay alguien?");
        await Task.Delay(TimeSpan.FromSeconds(3));

        // ORB-C07's last criterion. Without the flag on the conversation, this second
        // message would have reopened the thread and the assistant would have answered it.
        Assert.Equal(repliesAfterHandoff, await CountAgentRepliesAsync(conversationId));

        await using var db = _fixture.CreateOwnerDbContext();
        var conversation = await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == conversationId);
        Assert.Equal(ConversationStatus.Pending, conversation.Status);
        Assert.True(conversation.UnreadCount >= 2, "A person still has to see what arrived while it waited.");
    }

    [Fact]
    public async Task A_person_can_give_the_conversation_back_to_the_assistant()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);
        await EnableDefaultAgentAsync(client, tenantId, cookies);

        await SendInboundAsync(client, account.ExternalId, "573001110003", "quiero hablar con una persona");
        var queue = await WaitForQueueAsync(client, tenantId, cookies, expected: 1);
        var conversationId = queue.Items[0].ConversationId;

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/return-to-assistant", cookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = (await response.Content.ReadFromJsonAsync<HandoffStateDto>(TestRequests.JsonOptions))!;
        Assert.False(state.IsWaitingForHuman);
        Assert.Null(state.Reason);
        Assert.Null(state.Summary);

        Assert.Empty((await QueueAsync(client, tenantId, cookies)).Items);

        // Reactivated means reactivated: the next message is answered again.
        _fixture.Llm.NextReply = "Claro, te cuento.";
        var repliesBefore = await CountAgentRepliesAsync(conversationId);
        await SendInboundAsync(client, account.ExternalId, "573001110003", "¿a qué hora abren?");

        await Eventually.AssertAsync(async () => await CountAgentRepliesAsync(conversationId) > repliesBefore);

        // Doing it twice is not an error: the conversation is already where the caller
        // wants it, the same reasoning as discarding a draft that is not there.
        var again = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/return-to-assistant", cookies);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task A_viewer_can_read_the_queue_but_cannot_give_a_conversation_back()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, ownerCookies);
        await EnableDefaultAgentAsync(client, tenantId, ownerCookies);

        await SendInboundAsync(client, account.ExternalId, "573001110004", "quiero hablar con una persona");
        var queue = await WaitForQueueAsync(client, tenantId, ownerCookies, expected: 1);
        var conversationId = queue.Items[0].ConversationId;

        var (_, viewerCookies) = await TestRequests.InviteAndAcceptAsync(_fixture, client, tenantId, ownerCookies, MemberRole.Viewer);

        // Seeing who is waiting is not a privilege — every role gets ViewInbox.
        var read = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/handoffs", viewerCookies);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // Handing a customer back to a machine is acting on the conversation, which is the
        // same tier as replying to it.
        var act = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/conversations/{conversationId}/return-to-assistant", viewerCookies);
        Assert.Equal(HttpStatusCode.Forbidden, act.StatusCode);
    }

    [Fact]
    public async Task One_organizations_queue_never_shows_anothers()
    {
        var client = TestRequests.CreateClient(_fixture);

        var (_, _, victimTenant, victimCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Víctima");
        var victimAccount = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, victimTenant, victimCookies);
        await EnableDefaultAgentAsync(client, victimTenant, victimCookies);
        await SendInboundAsync(client, victimAccount.ExternalId, "573001110005", "quiero hablar con una persona");

        // Positive control: the queue does fill up for the tenant it belongs to. Without
        // it, an endpoint that always answered nothing would pass the isolation assertion.
        var victimQueue = await WaitForQueueAsync(client, victimTenant, victimCookies, expected: 1);
        var victimConversation = victimQueue.Items[0].ConversationId;

        var (_, _, otherTenant, otherCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra");

        Assert.Empty((await QueueAsync(client, otherTenant, otherCookies)).Items);

        // And the other tenant cannot reach into it by id either.
        var reach = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{otherTenant}/conversations/{victimConversation}/return-to-assistant", otherCookies);
        Assert.Equal(HttpStatusCode.NotFound, reach.StatusCode);

        // Untouched: still waiting for its own team.
        Assert.Single((await QueueAsync(client, victimTenant, victimCookies)).Items);
    }

    [Fact]
    public async Task The_queue_pages_and_says_how_many_are_waiting_in_total()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);
        await EnableDefaultAgentAsync(client, tenantId, cookies);

        await SendInboundAsync(client, account.ExternalId, "573001110006", "quiero hablar con una persona");
        await WaitForQueueAsync(client, tenantId, cookies, expected: 1);
        await SendInboundAsync(client, account.ExternalId, "573001110007", "quiero hablar con una persona");
        await WaitForQueueAsync(client, tenantId, cookies, expected: 2);

        var firstPage = await QueueAsync(client, tenantId, cookies, limit: 1);

        Assert.Single(firstPage.Items);
        Assert.NotNull(firstPage.NextCursor);

        // The total is the whole point of the field: a page of one out of two must not
        // read as "one person is waiting".
        Assert.Equal(2, firstPage.Total);

        var secondPage = await QueueAsync(client, tenantId, cookies, limit: 1, cursor: firstPage.NextCursor);

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.NotEqual(firstPage.Items[0].ConversationId, secondPage.Items[0].ConversationId);

        // Oldest wait first: the person who has been waiting longest is the one closest to
        // giving up, and newest-first starves exactly them.
        Assert.True(firstPage.Items[0].RequestedAt <= secondPage.Items[0].RequestedAt);
    }

    [Fact]
    public async Task The_assistant_can_hand_over_on_its_own_judgment_and_still_say_goodbye()
    {
        // The fourth trigger, and the only one a phrase list cannot see: the model decides
        // it cannot help. Its farewell does go out — that one mirrors the customer's
        // language, which the owner's stored sentence cannot.
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);
        await EnableToolsAsync(client, tenantId, cookies, agentId, AiToolCatalog.EscalarAHumano);

        _fixture.Llm.NextToolCalls.Add(new LlmToolCall(
            "call_1",
            AiToolCatalog.EscalarAHumano,
            """{"resumen":"Reclama por un pedido que llegó incompleto."}"""));
        _fixture.Llm.NextReply = "Te paso con alguien del equipo para que lo revise.";

        await SendInboundAsync(client, account.ExternalId, "573001110008", "mi pedido llegó incompleto y estoy cansado de explicarlo");

        var queue = await WaitForQueueAsync(client, tenantId, cookies, expected: 1);
        var waiting = Assert.Single(queue.Items);

        Assert.Equal(HandoffReason.AgentDecision, waiting.Reason);

        // The model's own note, not a generated one.
        Assert.Equal("Reclama por un pedido que llegó incompleto.", waiting.Summary);

        await Eventually.AssertAsync(async () =>
        {
            await using var db = _fixture.CreateOwnerDbContext();

            return await db.Messages.AsNoTracking().AnyAsync(m =>
                m.ConversationId == waiting.ConversationId
                && m.Body == "Te paso con alguien del equipo para que lo revise.");
        });
    }

    [Fact]
    public async Task Saving_the_limits_without_the_new_sentence_keeps_the_stored_one()
    {
        // The frontend's client sends { blockedTopics, outOfScopeReply } and predates
        // handoffReply. If the field were required, its limits screen would stop saving —
        // and a business owner would find that out, not us.
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);

        var custom = "Te dejo con el equipo, que te atiende mejor que yo.";
        (await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/guardrails", cookies,
            new { blockedTopics = Array.Empty<string>(), outOfScopeReply = "Eso lo ve el equipo.", handoffReply = custom }))
            .EnsureSuccessStatusCode();

        var withoutIt = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/guardrails", cookies,
            new { blockedTopics = new[] { "dosis" }, outOfScopeReply = "Eso lo ve el equipo." });

        Assert.Equal(HttpStatusCode.OK, withoutIt.StatusCode);

        // Read back in another request, so this is what was stored and not an echo.
        var reread = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}", cookies);
        var agent = (await reread.Content.ReadFromJsonAsync<AiAgentDto>(TestRequests.JsonOptions))!;

        Assert.Equal(custom, agent.Guardrails.HandoffReply);
        Assert.Equal(["dosis"], agent.Guardrails.BlockedTopics);
    }

    private static async Task<HandoffQueuePage> QueueAsync(
        HttpClient client, Guid tenantId, CookieJar cookies, int? limit = null, string? cursor = null)
    {
        var query = new List<string>();

        if (limit is { } take)
        {
            query.Add($"limit={take}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var url = $"/api/tenants/{tenantId}/handoffs" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        var response = await TestRequests.SendAsync(client, HttpMethod.Get, url, cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<HandoffQueuePage>(TestRequests.JsonOptions))!;
    }

    /// <summary>
    /// The handoff travels the real path — worker, outbox, assistant — so it arrives when
    /// it arrives. Polling the endpoint is also what the dashboard does, since there is no
    /// realtime client on the other side yet.
    /// </summary>
    private static async Task<HandoffQueuePage> WaitForQueueAsync(
        HttpClient client, Guid tenantId, CookieJar cookies, int expected)
    {
        HandoffQueuePage page = null!;

        await Eventually.AssertAsync(async () =>
        {
            page = await QueueAsync(client, tenantId, cookies, limit: 100);

            return page.Total >= expected;
        });

        return page;
    }

    /// <summary>
    /// A reply the model wrote, which on the wire is an outbound message carrying a run
    /// and no sender. The assistant's own system sentences are deliberately excluded:
    /// this counts whether it kept <em>answering</em>.
    /// </summary>
    private async Task<int> CountAgentRepliesAsync(Guid conversationId)
    {
        await using var db = _fixture.CreateOwnerDbContext();

        return await db.Messages.AsNoTracking().CountAsync(m =>
            m.ConversationId == conversationId
            && m.Direction == MessageDirection.Outbound
            && m.AiRunId != null);
    }

    private static async Task<Guid> EnableDefaultAgentAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var list = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents", cookies);
        var agents = (await list.Content.ReadFromJsonAsync<IReadOnlyList<AiAgentDto>>(TestRequests.JsonOptions))!;
        var agentId = agents[0].Id;

        (await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/ai-agents/{agentId}/enabled", cookies,
            new { isEnabled = true })).EnsureSuccessStatusCode();

        return agentId;
    }

    /// <summary>
    /// Tools are part of the draft (ORB-C10), so enabling one means saving and publishing.
    /// </summary>
    private static async Task EnableToolsAsync(
        HttpClient client, Guid tenantId, CookieJar cookies, Guid agentId, params string[] tools)
    {
        (await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/ai-agents/{agentId}", cookies,
            new
            {
                name = "Asistente",
                personality = "Amable y resolutivo.",
                instructions = "Pasa la conversación a una persona cuando no puedas resolver.",
                style = new { formality = "Balanced", verbosity = "Balanced", energy = "Balanced" },
                tools,
            })).EnsureSuccessStatusCode();

        (await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/publish", cookies)).EnsureSuccessStatusCode();
    }

    private static async Task SendInboundAsync(HttpClient client, string phoneNumberId, string fromWaId, string text)
    {
        var body = TextMessagePayload(phoneNumberId, fromWaId, $"wamid.{Guid.NewGuid():N}", text);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Content.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(body));

        (await client.SendAsync(request)).EnsureSuccessStatusCode();
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
                    "contacts": [{ "profile": { "name": "Laura Gómez" }, "wa_id": "{{fromWaId}}" }],
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
