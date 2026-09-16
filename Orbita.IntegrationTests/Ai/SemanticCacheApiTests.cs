using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Orbita.Application.Ai;
using Orbita.Application.Channels;
using Orbita.Domain.Inbox;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C12 against the real database, because the part worth proving is the part a fake
/// repository cannot show: that the reuse is decided by a pgvector search over one
/// assistant's entries, under RLS, and that changing the knowledge base stops it.
/// </summary>
public sealed class SemanticCacheApiTests : IClassFixture<TenantsApiFixture>
{
    private const string AppSecret = "test-app-secret";

    private readonly TenantsApiFixture _fixture;

    public SemanticCacheApiTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Llm.Reset();
    }

    [Fact]
    public async Task The_cache_is_off_until_the_owner_sets_a_threshold()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);

        var initial = await GetCacheAsync(client, tenantId, agentId, cookies);

        Assert.Null(initial.Threshold);
        Assert.Equal(0, initial.Hits);

        // Nobody has asked anything yet, which is not the same as "it never hits".
        Assert.Null(initial.HitRate);

        var set = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache", cookies, new { threshold = 0.95m });
        set.EnsureSuccessStatusCode();

        Assert.Equal(0.95m, (await GetCacheAsync(client, tenantId, agentId, cookies)).Threshold);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.5)]
    public async Task A_threshold_outside_the_useful_range_is_refused(decimal threshold)
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache", cookies, new { threshold });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_tenants_owner_cannot_read_or_change_it()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Acme Corp");
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);
        var (_, _, _, outsider) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Other Business");

        var read = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache", outsider);
        var write = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache", outsider, new { threshold = 0.95m });

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task The_same_question_from_a_second_customer_is_answered_without_a_model_call()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);
        await SetThresholdAsync(client, tenantId, agentId, cookies, 0.95m);

        _fixture.Llm.NextReply = "Abrimos de 7 a.m. a 7 p.m.";

        var first = await AskAsync(client, account.ExternalId, "573001110001", "¿hasta qué hora abren?");
        Assert.Equal("Abrimos de 7 a.m. a 7 p.m.", first.Body);
        Assert.Equal(1, CompletionCount());

        // A different customer, the same words. The fake embeds deterministically, so the
        // two vectors are identical and the search is the only thing deciding.
        _fixture.Llm.NextReply = "Esta respuesta no debería usarse.";
        var second = await AskAsync(client, account.ExternalId, "573001110002", "¿hasta qué hora abren?");

        Assert.Equal("Abrimos de 7 a.m. a 7 p.m.", second.Body);
        Assert.Equal(1, CompletionCount());

        // The reply still carries a run — the embedding that found it — so the ledger
        // explains every message, and the hit rate is readable from ai_runs alone.
        Assert.NotNull(second.AiRunId);

        var stats = await GetCacheAsync(client, tenantId, agentId, cookies);
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(0.5m, stats.HitRate);
    }

    [Fact]
    public async Task A_different_question_is_not_close_enough_to_reuse()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);
        await SetThresholdAsync(client, tenantId, agentId, cookies, 0.95m);

        _fixture.Llm.NextReply = "Abrimos de 7 a.m. a 7 p.m.";
        await AskAsync(client, account.ExternalId, "573002220001", "¿hasta qué hora abren?");

        _fixture.Llm.NextReply = "Sí, hacemos domicilios en toda la ciudad.";
        var second = await AskAsync(client, account.ExternalId, "573002220002", "¿hacen domicilios?");

        Assert.Equal("Sí, hacemos domicilios en toda la ciudad.", second.Body);
        Assert.Equal(2, CompletionCount());
    }

    [Fact]
    public async Task One_tenants_answers_are_never_offered_to_another()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, firstTenant, firstCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Panadería Uno");
        var firstAccount = await ConnectAsync(client, firstTenant, firstCookies);
        var firstAgent = await EnableDefaultAgentAsync(client, firstTenant, firstCookies);
        await SetThresholdAsync(client, firstTenant, firstAgent, firstCookies, 0.95m);

        _fixture.Llm.NextReply = "El pedido mínimo es de 50.000 pesos.";
        await AskAsync(client, firstAccount.ExternalId, "573003330001", "¿cuál es el pedido mínimo?");

        var (_, _, secondTenant, secondCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Panadería Dos");
        var secondAccount = await ConnectAsync(client, secondTenant, secondCookies);
        var secondAgent = await EnableDefaultAgentAsync(client, secondTenant, secondCookies);
        await SetThresholdAsync(client, secondTenant, secondAgent, secondCookies, 0.95m);

        _fixture.Llm.NextReply = "El pedido mínimo acá es de 20.000 pesos.";
        var reply = await AskAsync(client, secondAccount.ExternalId, "573003330002", "¿cuál es el pedido mínimo?");

        // The same question, word for word, and the other tenant's price must not leak.
        Assert.Equal("El pedido mínimo acá es de 20.000 pesos.", reply.Body);
        Assert.Equal(0, (await GetCacheAsync(client, secondTenant, secondAgent, secondCookies)).Hits);

        // Positive control: within one tenant the reuse this test is about does happen, so
        // a broken cache cannot pass the isolation assertion by never hitting at all.
        _fixture.Llm.NextReply = "Respuesta que no debería usarse.";
        var reused = await AskAsync(client, secondAccount.ExternalId, "573003330003", "¿cuál es el pedido mínimo?");
        Assert.Equal("El pedido mínimo acá es de 20.000 pesos.", reused.Body);
    }

    [Fact]
    public async Task Publishing_new_instructions_stops_the_old_answers_from_coming_back()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var account = await ConnectAsync(client, tenantId, cookies);
        var agentId = await EnableDefaultAgentAsync(client, tenantId, cookies);
        await SetThresholdAsync(client, tenantId, agentId, cookies, 0.95m);

        _fixture.Llm.NextReply = "El domicilio es gratis.";
        await AskAsync(client, account.ExternalId, "573004440001", "¿cobran domicilio?");

        await PublishInstructionsAsync(client, tenantId, cookies, agentId, "Ahora cobramos 5.000 de domicilio.");

        _fixture.Llm.NextReply = "El domicilio cuesta 5.000.";
        var reply = await AskAsync(client, account.ExternalId, "573004440002", "¿cobran domicilio?");

        // The old sentence is now wrong, and nothing deleted it — the fingerprint simply
        // no longer matches, so it can never be found again.
        Assert.Equal("El domicilio cuesta 5.000.", reply.Body);
    }

    private int CompletionCount()
    {
        lock (_fixture.Llm.CompletionRequests)
        {
            return _fixture.Llm.CompletionRequests.Count;
        }
    }

    /// <summary>One customer's message, all the way to the assistant's reply.</summary>
    private async Task<Message> AskAsync(HttpClient client, string phoneNumberId, string fromWaId, string text)
    {
        var externalId = $"wamid.{Guid.NewGuid():N}";
        await SendWebhookAsync(client, TextMessagePayload(phoneNumberId, fromWaId, externalId, text));

        var inbound = await WaitForAsync(async db =>
            await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalId == externalId));

        return await WaitForAsync(async db => await db.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.ConversationId == inbound.ConversationId && m.Direction == MessageDirection.Outbound));
    }

    private async Task<T> WaitForAsync<T>(Func<Orbita.Infrastructure.Persistence.OrbitaDbContext, Task<T?>> read)
        where T : class
    {
        T? found = null;

        await Eventually.AssertAsync(async () =>
        {
            await using var db = _fixture.CreateOwnerDbContext();
            found = await read(db);

            return found is not null;
        });

        return found!;
    }

    private static async Task<SemanticCacheDto> GetCacheAsync(HttpClient client, Guid tenantId, Guid agentId, CookieJar cookies)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache", cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SemanticCacheDto>(TestRequests.JsonOptions))!;
    }

    private static async Task SetThresholdAsync(
        HttpClient client, Guid tenantId, Guid agentId, CookieJar cookies, decimal threshold)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Put, $"/api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache", cookies, new { threshold });
        response.EnsureSuccessStatusCode();
    }

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

    /// <summary>A draft plus a publish, which is the only way live instructions change.</summary>
    private static async Task PublishInstructionsAsync(
        HttpClient client, Guid tenantId, CookieJar cookies, Guid agentId, string instructions)
    {
        var draft = await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/ai-agents/{agentId}", cookies,
            new
            {
                name = "Asistente",
                personality = "Amable y resolutivo.",
                instructions,
                style = new { formality = "Balanced", verbosity = "Balanced", energy = "Balanced" },
                tools = Array.Empty<string>(),
            });
        draft.EnsureSuccessStatusCode();

        var publish = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/ai-agents/{agentId}/publish", cookies);
        publish.EnsureSuccessStatusCode();
    }

    private Task<ChannelAccountDto> ConnectAsync(HttpClient client, Guid tenantId, CookieJar cookies)
        => TestRequests.ConnectVerifiedWhatsAppAsync(_fixture, client, tenantId, cookies);

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
