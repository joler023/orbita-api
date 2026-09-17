using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Ai;
using Orbita.Application.Inbox;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Inbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C04. Two things matter here and they pull in opposite directions: what reaches the
/// model when the assistant does answer, and every reason it must not answer at all. The
/// second list is the longer one, and it is the one a customer notices when it is wrong —
/// an assistant talking over a person, or into a closed service window, is worse than an
/// assistant that stays quiet.
/// </summary>
public sealed class AgentConversationResponderTests
{
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IAiAgentRepository> _agents = new();
    private readonly Mock<IKnowledgeSearchService> _search = new();
    private readonly Mock<ILlmProvider> _llm = new();
    private readonly Mock<IAiRunRecorder> _runs = new();
    private readonly Mock<IOutboundMessageService> _outbound = new();
    private readonly Mock<Orbita.Application.Outbox.IOutboxWriter> _outbox = new();
    private readonly Mock<IAgentToolExecutor> _tools = new();
    private readonly Mock<IConversationHandoffService> _handoffs = new();
    private readonly Mock<IRoutingRuleRepository> _routing = new();
    private readonly Mock<Orbita.Domain.Channels.IChannelAccountRepository> _channelAccounts = new();
    private readonly Mock<Orbita.Domain.Tenants.ITenantRepository> _tenants = new();
    private readonly Mock<IAgentAnswerCache> _answerCache = new();
    private readonly PassThroughUnitOfWork _unitOfWork = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();

    private readonly AgentConversationResponder _sut;

    private LlmCompletionRequest? _lastRequest;

    public AgentConversationResponderTests()
    {
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback((LlmCompletionRequest request, CancellationToken _) => _lastRequest = request)
            .ReturnsAsync(new LlmCompletionResult("Sí, abrimos hasta las 7.", [], Usage));

        _runs
            .Setup(r => r.Record(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmUsage>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<bool>()))
            .Returns(_runId);

        _handoffs
            .Setup(h => h.RequestAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<HandoffReason>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _search
            .Setup(s => s.SearchForAgentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Hit("Horarios", "Abrimos de 7 a.m. a 7 p.m.")]);

        _outbound
            .Setup(o => o.SendAgentReplyAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MessageDto.From(
                Message.OutboundAgentText(_tenantId, Guid.NewGuid(), "Sí, abrimos hasta las 7.", _runId, _time.GetUtcNow())));

        _outbound
            .Setup(o => o.SendSystemReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MessageDto.From(
                Message.OutboundText(_tenantId, Guid.NewGuid(), "sistema", null, MessageCategory.Service, _time.GetUtcNow())));

        _tools.Setup(t => t.DefinitionsFor(It.IsAny<AiAgent>())).Returns([]);
        _routing.Setup(r => r.ListByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _sut = new AgentConversationResponder(
            _conversations.Object,
            _messages.Object,
            _agents.Object,
            _search.Object,
            _llm.Object,
            _runs.Object,
            _outbound.Object,
            _outbox.Object,
            _tools.Object,
            _handoffs.Object,
            _routing.Object,
            _channelAccounts.Object,
            _tenants.Object,
            _answerCache.Object,
            _unitOfWork,
            _time,
            NullLogger<AgentConversationResponder>.Instance);
    }

    private static LlmUsage Usage => new("openai/gpt-5.6-luna", 420, 60, 0.00016m, 1200, "stop");

    private static KnowledgeSearchHit Hit(string title, string content)
        => new(Guid.NewGuid(), Guid.NewGuid(), title, 0, content, 0.93);

    private AiAgent Agent(bool enabled = true, bool withKnowledgeTool = true)
    {
        var agent = AiAgent.Create(
            _tenantId,
            "Espiga",
            "Cercano y resolutivo.",
            "Confirmá la hora de recogida.",
            AgentStyle.Default,
            _time.GetUtcNow());

        if (withKnowledgeTool)
        {
            agent.EnableTools([AiToolCatalog.ConsultarConocimiento]);
        }

        // An assistant is born switched off (ORB-C10: publishing does not turn it on), so
        // "enabled" is something a test has to ask for.
        agent.SetEnabled(enabled);

        _agents.Setup(r => r.GetByIdAsync(_tenantId, agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _agents.Setup(r => r.FindEnabledByTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabled ? agent : null);

        return agent;
    }

    /// <summary>An open conversation with a live 24h window and one inbound message in it.</summary>
    private (Conversation Conversation, Message Inbound) OpenConversation(string body = "¿Hasta qué hora abren?")
    {
        var conversation = Conversation.Open(_tenantId, Guid.NewGuid(), Guid.NewGuid(), _time.GetUtcNow());
        conversation.RegisterInbound(_time.GetUtcNow(), body);

        var inbound = Message.Inbound(
            _tenantId, conversation.Id, "wamid.test.1", body, null, null, _time.GetUtcNow());

        _conversations.Setup(r => r.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _messages.Setup(r => r.GetByIdAsync(inbound.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inbound);
        _messages
            .Setup(r => r.GetRecentByConversationAsync(_tenantId, conversation.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([inbound]);

        return (conversation, inbound);
    }

    private Task<AgentReplyOutcome> RespondAsync(Conversation conversation, Message inbound)
        => _sut.RespondAsync(_tenantId, conversation.Id, inbound.Id, CancellationToken.None);

    /// <summary>ORB-C12: an assistant with a threshold set, and a cache that answers as told.</summary>
    private AiAgent CachingAgent(AgentAnswerCacheHit? hit)
    {
        var agent = Agent();
        agent.SetSemanticCacheLevel(SemanticCacheLevel.Balanced);

        _answerCache
            .Setup(c => c.LookupAsync(_tenantId, agent, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentCacheLookup(hit, [0.1f, 0.2f], "fingerprint", _runId));

        return agent;
    }

    [Fact]
    public async Task A_close_enough_question_is_answered_from_the_cache_without_a_model_call()
    {
        var agent = CachingAgent(new AgentAnswerCacheHit(Guid.NewGuid(), "Abrimos hasta las 7.", 0.97));
        var (conversation, inbound) = OpenConversation();

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.Replied, outcome.Decision);
        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // The saving is the point, but the reply still goes out the one way replies go out,
        // carrying the embedding run that found it so the ledger still explains the message.
        _outbound.Verify(
            o => o.SendAgentReplyAsync(_tenantId, conversation.Id, "Abrimos hasta las 7.", _runId, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(agent.Id, conversation.AiAgentId);
    }

    [Fact]
    public async Task A_miss_answers_normally_and_keeps_the_answer_for_next_time()
    {
        var agent = CachingAgent(hit: null);
        var (conversation, inbound) = OpenConversation();

        await RespondAsync(conversation, inbound);

        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _answerCache.Verify(
            c => c.Store(_tenantId, agent, It.IsAny<AgentCacheLookup>(), "¿Hasta qué hora abren?", "Sí, abrimos hasta las 7."),
            Times.Once);
    }

    [Fact]
    public async Task An_answer_that_acted_on_the_crm_is_never_kept()
    {
        // Replaying "ya registré tu pedido" to a different customer would claim an action
        // that never happened for them.
        var agent = CachingAgent(hit: null);
        agent.EnableTools([AiToolCatalog.ConsultarConocimiento, AiToolCatalog.CrearOportunidad]);
        var (conversation, inbound) = OpenConversation();

        _tools.Setup(t => t.DefinitionsFor(It.IsAny<AiAgent>()))
            .Returns([new LlmTool(AiToolCatalog.CrearOportunidad, "Registra una oportunidad.", "{}")]);
        _tools
            .Setup(t => t.ExecuteAsync(It.IsAny<AgentToolContext>(), It.IsAny<LlmToolCall>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentToolResult(AiToolCatalog.CrearOportunidad, true, """{"ok":true}"""));

        var calls = 0;
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => calls++ == 0
                ? new LlmCompletionResult(null, [new LlmToolCall("c1", AiToolCatalog.CrearOportunidad, "{}")], Usage)
                : new LlmCompletionResult("Listo, queda registrado.", [], Usage));

        await RespondAsync(conversation, inbound);

        _answerCache.Verify(
            c => c.Store(It.IsAny<Guid>(), It.IsAny<AiAgent>(), It.IsAny<AgentCacheLookup>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Later_turns_are_neither_answered_from_the_cache_nor_kept()
    {
        // The second answer in a conversation was shaped by the first; it answers that
        // exchange, not the question on its own.
        var agent = CachingAgent(new AgentAnswerCacheHit(Guid.NewGuid(), "Abrimos hasta las 7.", 0.99));
        var (conversation, inbound) = OpenConversation();

        var earlier = Message.Inbound(_tenantId, conversation.Id, "wamid.test.0", "hola", null, null, _time.GetUtcNow());
        _messages
            .Setup(r => r.GetRecentByConversationAsync(_tenantId, conversation.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([earlier, inbound]);

        await RespondAsync(conversation, inbound);

        _answerCache.Verify(
            c => c.LookupAsync(It.IsAny<Guid>(), It.IsAny<AiAgent>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_assistant_without_a_threshold_never_consults_the_cache()
    {
        var agent = Agent();
        var (conversation, inbound) = OpenConversation();

        await RespondAsync(conversation, inbound);

        Assert.Equal(SemanticCacheLevel.Off, agent.SemanticCacheLevel);
        _answerCache.Verify(
            c => c.LookupAsync(It.IsAny<Guid>(), It.IsAny<AiAgent>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task It_answers_and_sends_through_the_normal_outbound_queue()
    {
        var agent = Agent();
        var (conversation, inbound) = OpenConversation();

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.Replied, outcome.Decision);

        // The acceptance criterion is explicit that the reply leaves by the same queue a
        // person's message does, so it inherits the retry policy and the rate limit.
        _outbound.Verify(
            o => o.SendAgentReplyAsync(_tenantId, conversation.Id, "Sí, abrimos hasta las 7.", _runId, It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Equal(agent.Id, conversation.AiAgentId);
    }

    [Fact]
    public async Task Every_call_is_measured_against_the_conversation_it_answered()
    {
        var agent = Agent();
        var (conversation, inbound) = OpenConversation();

        await RespondAsync(conversation, inbound);

        // Without the conversation id the run exists but ORB-A13 cannot attribute it, and
        // ORB-D11's "desglose por agente" has nothing to group by.
        _runs.Verify(r => r.Record(_tenantId, agent.Id, It.IsAny<LlmUsage>(), conversation.Id, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<Guid>?>()), Times.Once);
    }

    [Fact]
    public async Task The_prompt_carries_the_standing_rules_and_the_retrieved_passages()
    {
        Agent();
        var (conversation, inbound) = OpenConversation();

        await RespondAsync(conversation, inbound);

        Assert.NotNull(_lastRequest);
        var system = string.Join("\n", _lastRequest!.Messages.Where(m => m.Role == LlmMessageRole.System).Select(m => m.Content));

        Assert.Contains("mismo idioma", system, StringComparison.Ordinal);
        Assert.Contains("escribirle al equipo", system, StringComparison.Ordinal);

        // Since ORB-C07 the assistant may say it is leaving the conversation with the
        // team, because that now happens. What it still must never promise is a reply:
        // the queue is real, but ORB-B15 assigns nobody to it, so "alguien te escribe
        // enseguida" would be the promise C06 already had to walk back once.
        Assert.Contains("dejas la conversación con el equipo", system, StringComparison.Ordinal);
        Assert.Contains("Nunca prometas cuándo le responden", system, StringComparison.Ordinal);

        // Órbita serves Colombia, Mexico and Spain: the assistant mirrors how the
        // customer writes instead of imposing one treatment on all three.
        Assert.Contains("de tú, de vos o de usted", system, StringComparison.Ordinal);
        Assert.Contains("Abrimos de 7 a.m. a 7 p.m.", system, StringComparison.Ordinal);

        // The message being answered is the last turn, and it appears exactly once even
        // though the history read returns it too.
        Assert.Equal("¿Hasta qué hora abren?", _lastRequest.Messages[^1].Content);
        Assert.Single(_lastRequest.Messages, m => m.Content == "¿Hasta qué hora abren?");
    }

    [Fact]
    public async Task An_assistant_without_the_knowledge_tool_answers_ungrounded()
    {
        Agent(withKnowledgeTool: false);
        var (conversation, inbound) = OpenConversation();

        await RespondAsync(conversation, inbound);

        // Switching the tool off has to actually switch it off, or the switch is a lie.
        _search.Verify(
            s => s.SearchForAgentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task It_stays_quiet_outside_the_service_window()
    {
        Agent();
        var (conversation, inbound) = OpenConversation();
        _time.Advance(TimeSpan.FromHours(25));

        var outcome = await RespondAsync(conversation, inbound);

        // A free-form reply out here would be rejected by Meta with 131047 — paying a
        // model to produce a message that cannot be delivered is the worst of both.
        Assert.Equal(AgentReplyDecision.ServiceWindowClosed, outcome.Decision);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task It_stays_quiet_when_the_owner_switched_the_assistant_off()
    {
        Agent(enabled: false);
        var (conversation, inbound) = OpenConversation();

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.NoAgentAssigned, outcome.Decision);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task It_stays_quiet_when_the_tenant_has_no_assistant_at_all()
    {
        _agents.Setup(r => r.FindEnabledByTenantAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync((AiAgent?)null);
        var (conversation, inbound) = OpenConversation();

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.NoAgentAssigned, outcome.Decision);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task It_stays_quiet_when_there_is_no_text_to_answer()
    {
        Agent();
        var conversation = Conversation.Open(_tenantId, Guid.NewGuid(), Guid.NewGuid(), _time.GetUtcNow());
        conversation.RegisterInbound(_time.GetUtcNow(), null);

        // A photo with no caption. Answering "no entendí" to every image a customer sends
        // is worse than letting a person look at it.
        var inbound = Message.Inbound(_tenantId, conversation.Id, "wamid.img", null, "image/jpeg", null, _time.GetUtcNow());
        _conversations.Setup(r => r.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _messages.Setup(r => r.GetByIdAsync(inbound.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inbound);

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.NothingToAnswer, outcome.Decision);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task It_never_answers_its_own_outbound_message()
    {
        Agent();
        var conversation = Conversation.Open(_tenantId, Guid.NewGuid(), Guid.NewGuid(), _time.GetUtcNow());
        conversation.RegisterInbound(_time.GetUtcNow(), "hola");

        var outbound = Message.OutboundAgentText(_tenantId, conversation.Id, "Hola, ¿en qué te ayudo?", _runId, _time.GetUtcNow());
        _conversations.Setup(r => r.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _messages.Setup(r => r.GetByIdAsync(outbound.Id, It.IsAny<CancellationToken>())).ReturnsAsync(outbound);

        var outcome = await _sut.RespondAsync(_tenantId, conversation.Id, outbound.Id, CancellationToken.None);

        // Otherwise the assistant answers itself, forever, at a cost per turn.
        Assert.Equal(AgentReplyDecision.NotAnInboundMessage, outcome.Decision);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task A_model_that_answers_with_nothing_still_gets_billed_but_sends_nothing()
    {
        Agent();
        var (conversation, inbound) = OpenConversation();
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult("   ", [], Usage));

        var outcome = await RespondAsync(conversation, inbound);

        // The call was made, so it is owed to the ledger whatever came back. Since
        // ORB-C07 the customer also stops waiting on an assistant with nothing to say —
        // see A_model_that_answers_with_nothing_hands_the_customer_to_a_person.
        Assert.Equal(AgentReplyDecision.HandedOffToHuman, outcome.Decision);
        _runs.Verify(r => r.Record(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmUsage>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<Guid>?>()), Times.Once);
        Assert.Equal(1, _unitOfWork.SaveCount);
        _outbound.Verify(
            o => o.SendAgentReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_blocked_topic_gets_the_owners_sentence_and_no_model_call()
    {
        var agent = Agent();
        agent.SetGuardrails(["dosis"], "Eso lo ve mejor el equipo médico, escríbeles directamente.", AiAgent.DefaultHandoffReply);
        var (conversation, inbound) = OpenConversation("¿Qué dosis de ibuprofeno me tomo?");

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.BlockedByGuardrail, outcome.Decision);
        Assert.Equal(GuardrailReason.OutOfScopeTopic, outcome.GuardrailReason);

        // The owner's words, not ours — and nothing paid for, because the point of
        // blocking before the call is that the model never gets near the subject.
        _outbound.Verify(
            o => o.SendSystemReplyAsync(_tenantId, conversation.Id, "Eso lo ve mejor el equipo médico, escríbeles directamente.", It.IsAny<CancellationToken>()),
            Times.Once);
        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Every_block_is_recorded_with_the_topic_that_caused_it()
    {
        var agent = Agent();
        agent.SetGuardrails(["dosis"], AiAgent.DefaultOutOfScopeReply, AiAgent.DefaultHandoffReply);
        var (conversation, inbound) = OpenConversation("¿qué dosis tomo?");

        await RespondAsync(conversation, inbound);

        // A silenced assistant is the hardest thing in the product to diagnose from
        // outside, so the record has to name the word that did it.
        _outbox.Verify(
            o => o.StageAsync(
                _tenantId, nameof(AiAgent), agent.Id, "agent.reply_blocked",
                It.Is<object>(payload => payload.ToString()!.Contains("dosis")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_conversation_that_has_had_enough_answers_in_the_window_is_left_alone()
    {
        Agent();
        var (conversation, inbound) = OpenConversation();
        _messages
            .Setup(r => r.CountAgentRepliesSinceAsync(_tenantId, conversation.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentGuardrails.MaxRepliesPerWindow);

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(GuardrailReason.TooManyRepliesInWindow, outcome.GuardrailReason);

        // Silence, not a sentence: the assistant has already said too much, and one more
        // line would be the failure repeating itself.
        _outbound.Verify(
            o => o.SendSystemReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_reply_identical_to_the_previous_one_is_not_sent()
    {
        Agent();
        var (conversation, inbound) = OpenConversation();
        var earlier = Message.OutboundAgentText(_tenantId, conversation.Id, "Sí, abrimos hasta las 7.", _runId, _time.GetUtcNow());
        _messages
            .Setup(r => r.GetRecentByConversationAsync(_tenantId, conversation.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([earlier, inbound]);

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(GuardrailReason.RepeatedItself, outcome.GuardrailReason);
        _outbound.Verify(
            o => o.SendAgentReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Paid for, so still owed to the ledger even though nothing went out.
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_tool_call_runs_and_the_model_is_asked_again_with_the_result()
    {
        Agent();
        var (conversation, inbound) = OpenConversation("quiero cotizar una torta para 30 personas");
        var tool = new LlmTool(AiToolCatalog.CrearOportunidad, "crea", "{}");
        _tools.Setup(t => t.DefinitionsFor(It.IsAny<AiAgent>())).Returns([tool]);

        var call = new LlmToolCall("call_1", AiToolCatalog.CrearOportunidad, """{"titulo":"Torta 30 personas"}""");
        _llm.SetupSequence(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult(null, [call], Usage))
            .ReturnsAsync(new LlmCompletionResult("Listo, la dejé registrada.", [], Usage));
        _tools
            .Setup(t => t.ExecuteAsync(It.IsAny<AgentToolContext>(), call, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentToolResult(AiToolCatalog.CrearOportunidad, true, """{"ok":true}"""));

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.Replied, outcome.Decision);

        // The tool acts on this conversation's contact and tenant — fixed by us, not the model.
        _tools.Verify(t => t.ExecuteAsync(
            It.Is<AgentToolContext>(c => c.TenantId == _tenantId && c.ContactId == conversation.ContactId && c.ConversationId == conversation.Id),
            call, It.IsAny<CancellationToken>()), Times.Once);

        // Two paid calls, two runs.
        _runs.Verify(r => r.Record(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmUsage>(), conversation.Id, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<Guid>?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_model_that_keeps_calling_tools_is_cut_off_and_still_answers()
    {
        Agent();
        var (conversation, inbound) = OpenConversation("hola");
        _tools.Setup(t => t.DefinitionsFor(It.IsAny<AiAgent>())).Returns([new LlmTool(AiToolCatalog.MoverEtapa, "mueve", "{}")]);
        _tools
            .Setup(t => t.ExecuteAsync(It.IsAny<AgentToolContext>(), It.IsAny<LlmToolCall>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentToolResult(AiToolCatalog.MoverEtapa, false, """{"ok":false}"""));

        var requests = new List<LlmCompletionRequest>();
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback((LlmCompletionRequest request, CancellationToken _) => requests.Add(request))
            .ReturnsAsync((LlmCompletionRequest request, CancellationToken _) => request.Tools is { Count: > 0 }
                ? new LlmCompletionResult(null, [new LlmToolCall(Guid.NewGuid().ToString(), AiToolCatalog.MoverEtapa, """{"etapa":"X"}""")], Usage)
                : new LlmCompletionResult("Te ayudo con eso.", [], Usage));

        var outcome = await RespondAsync(conversation, inbound);

        // Bounded: three rounds with tools, then one without, and the customer gets text.
        Assert.Equal(AgentReplyDecision.Replied, outcome.Decision);
        Assert.Equal(AgentConversationResponder.MaxToolRounds + 1, requests.Count);
        Assert.Null(requests[^1].Tools);
    }

    [Fact]
    public async Task Each_run_records_the_tools_it_called_and_the_fragments_it_used()
    {
        Agent();
        var (conversation, inbound) = OpenConversation("quiero una torta");
        _tools.Setup(t => t.DefinitionsFor(It.IsAny<AiAgent>())).Returns([new LlmTool(AiToolCatalog.CrearOportunidad, "crea", "{}")]);

        var call = new LlmToolCall("call_1", AiToolCatalog.CrearOportunidad, """{"titulo":"Torta"}""");
        _llm.SetupSequence(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult(null, [call], Usage))
            .ReturnsAsync(new LlmCompletionResult("Listo.", [], Usage));
        _tools
            .Setup(t => t.ExecuteAsync(It.IsAny<AgentToolContext>(), It.IsAny<LlmToolCall>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentToolResult(AiToolCatalog.CrearOportunidad, true, """{"ok":true}"""));

        var recorded = new List<(IReadOnlyList<string>? Tools, IReadOnlyList<Guid>? Chunks)>();
        _runs
            .Setup(r => r.Record(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmUsage>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<bool>()))
            .Callback((Guid _, Guid _, LlmUsage _, Guid? _, IReadOnlyList<string>? tools, IReadOnlyList<Guid>? chunks, bool _) => recorded.Add((tools, chunks)))
            .Returns(_runId);

        await RespondAsync(conversation, inbound);

        // ORB-C09: "cada ejecución escribe... herramientas invocadas". One run per call, so
        // the tools belong to the call that asked for them.
        Assert.Equal(2, recorded.Count);
        Assert.Equal([AiToolCatalog.CrearOportunidad], recorded[0].Tools!);
        Assert.Empty(recorded[1].Tools!);

        // Retrieved once, recorded once — a sum over runs must not double-count it.
        Assert.Single(recorded[0].Chunks!);
        Assert.Null(recorded[1].Chunks);
    }

    [Fact]
    public async Task A_rule_can_route_a_new_conversation_to_the_team()
    {
        Agent();
        var (conversation, inbound) = OpenConversation("necesito facturación electrónica");
        _routing
            .Setup(r => r.ListByTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([RoutingRule.Create(_tenantId, 0, "Facturación al equipo", null, "facturación", agentId: null, _time.GetUtcNow())]);

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.LeftForTeamByRule, outcome.Decision);
        Assert.Null(conversation.AiAgentId);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task A_rule_picks_which_assistant_takes_a_new_conversation()
    {
        Agent();
        var sales = AiAgent.Create(_tenantId, "Ventas", "Amable.", "Vende.", AgentStyle.Default, _time.GetUtcNow());
        sales.SetEnabled(true);
        _agents.Setup(r => r.GetByIdAsync(_tenantId, sales.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sales);

        var (conversation, inbound) = OpenConversation("quiero comprar al por mayor");
        _routing
            .Setup(r => r.ListByTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([RoutingRule.Create(_tenantId, 0, "Mayoristas", null, "mayor", sales.Id, _time.GetUtcNow())]);

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.Replied, outcome.Decision);
        Assert.Equal(sales.Id, conversation.AiAgentId);
    }

    [Fact]
    public async Task Outside_its_hours_an_assistant_set_to_leave_it_for_the_team_stays_quiet()
    {
        var agent = Agent();
        // Monday 09:00–18:00 in UTC; the clock says 12:00 UTC on a Tuesday.
        agent.SetBusinessHours(BusinessHours.Create(
            [new BusinessHoursSlot(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(18, 0))],
            OutsideHoursBehavior.LeaveForTeam));

        var (conversation, inbound) = OpenConversation();

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.OutsideBusinessHours, outcome.Decision);

        // Not claimed: an assistant that will not answer must not take the conversation.
        Assert.Null(conversation.AiAgentId);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task A_customer_asking_for_a_person_is_handed_over_without_a_model_call()
    {
        var agent = Agent();
        var (conversation, inbound) = OpenConversation("necesito hablar con una persona");

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.HandedOffToHuman, outcome.Decision);
        Assert.Equal(HandoffReason.CustomerAsked, outcome.HandoffReason);

        _handoffs.Verify(
            h => h.RequestAsync(_tenantId, conversation.Id, agent.Id, HandoffReason.CustomerAsked, null, It.IsAny<CancellationToken>()),
            Times.Once);

        // The owner's sentence, and nothing paid for: asking for a person is read off the
        // message, not decided by a model.
        _outbound.Verify(
            o => o.SendSystemReplyAsync(_tenantId, conversation.Id, AiAgent.DefaultHandoffReply, It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task A_customer_told_once_is_not_told_again_on_every_message()
    {
        Agent();
        var (conversation, inbound) = OpenConversation("quiero hablar con alguien");

        // The queue already has it, so this call moved nothing.
        _handoffs
            .Setup(h => h.RequestAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<HandoffReason>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await RespondAsync(conversation, inbound);

        _outbound.Verify(
            o => o.SendSystemReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Asking_the_same_thing_months_apart_is_not_frustration()
    {
        // A conversation here is a lifetime thread, so the repetition trigger only looks
        // inside the current 24h window. Without that bound, a customer who asks the same
        // question once a month would be handed to a person for it — found while measuring
        // against seeded data, where long threads legitimately repeat a question.
        Agent();
        var (conversation, inbound) = OpenConversation("¿Aceptan transferencia?");

        var old = _time.GetUtcNow().AddDays(-40);
        _messages
            .Setup(r => r.GetRecentByConversationAsync(_tenantId, conversation.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Message.Inbound(_tenantId, conversation.Id, "wamid.old.1", "¿Aceptan transferencia?", null, null, old),
                Message.Inbound(_tenantId, conversation.Id, "wamid.old.2", "¿Aceptan transferencia?", null, null, old.AddDays(20)),
                inbound,
            ]);

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.Replied, outcome.Decision);
        _handoffs.Verify(
            h => h.RequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<HandoffReason>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Asking_the_same_thing_three_times_in_one_window_hands_it_over()
    {
        Agent();
        var (conversation, inbound) = OpenConversation("¿Aceptan transferencia?");

        var now = _time.GetUtcNow();
        _messages
            .Setup(r => r.GetRecentByConversationAsync(_tenantId, conversation.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Message.Inbound(_tenantId, conversation.Id, "wamid.w.1", "¿Aceptan transferencia?", null, null, now.AddMinutes(-20)),
                Message.Inbound(_tenantId, conversation.Id, "wamid.w.2", "¿Aceptan transferencia?", null, null, now.AddMinutes(-10)),
                inbound,
            ]);

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.HandedOffToHuman, outcome.Decision);
        Assert.Equal(HandoffReason.Frustration, outcome.HandoffReason);
    }

    [Fact]
    public async Task A_conversation_waiting_for_a_person_is_left_alone()
    {
        Agent();
        var (conversation, inbound) = OpenConversation();
        conversation.RequestHumanHandoff(HandoffReason.CustomerAsked, "Pidió una persona.", _time.GetUtcNow());

        var outcome = await RespondAsync(conversation, inbound);

        // ORB-C07's last criterion: nothing brings the assistant back except a human.
        Assert.Equal(AgentReplyDecision.HumanIsHandlingIt, outcome.Decision);
        VerifyNothingWasSpent();
    }

    [Fact]
    public async Task A_blocked_topic_also_leaves_the_conversation_with_a_person()
    {
        var agent = Agent();
        agent.SetGuardrails(["dosis"], AiAgent.DefaultOutOfScopeReply, AiAgent.DefaultHandoffReply);
        var (conversation, inbound) = OpenConversation("¿qué dosis tomo?");

        await RespondAsync(conversation, inbound);

        // Before ORB-C07 this answered the owner's sentence and left the customer with the
        // assistant that had just declined to help them.
        _handoffs.Verify(
            h => h.RequestAsync(_tenantId, conversation.Id, agent.Id, HandoffReason.OutOfScopeTopic, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_model_that_answers_with_nothing_hands_the_customer_to_a_person()
    {
        var agent = Agent();
        var (conversation, inbound) = OpenConversation();
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult("   ", [], Usage));

        var outcome = await RespondAsync(conversation, inbound);

        Assert.Equal(AgentReplyDecision.HandedOffToHuman, outcome.Decision);
        _handoffs.Verify(
            h => h.RequestAsync(_tenantId, conversation.Id, agent.Id, HandoffReason.AgentDecision, null, It.IsAny<CancellationToken>()),
            Times.Once);

        // Silently: an apology for a failure the customer has not seen is one more message
        // from an assistant that just proved it has nothing to say.
        _outbound.Verify(
            o => o.SendSystemReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A skipped reply must not reach the model at all. Asserting on the send alone would
    /// miss the expensive half: a call that was paid for and then thrown away.
    /// </summary>
    private void VerifyNothingWasSpent()
    {
        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _outbound.Verify(
            o => o.SendAgentReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
