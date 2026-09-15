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
            .Setup(r => r.Record(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmUsage>(), It.IsAny<Guid?>()))
            .Returns(_runId);

        _search
            .Setup(s => s.SearchForAgentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Hit("Horarios", "Abrimos de 7 a.m. a 7 p.m.")]);

        _outbound
            .Setup(o => o.SendAgentReplyAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MessageDto.From(
                Message.OutboundAgentText(_tenantId, Guid.NewGuid(), "Sí, abrimos hasta las 7.", _runId, _time.GetUtcNow())));

        _sut = new AgentConversationResponder(
            _conversations.Object,
            _messages.Object,
            _agents.Object,
            _search.Object,
            _llm.Object,
            _runs.Object,
            _outbound.Object,
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
        _runs.Verify(r => r.Record(_tenantId, agent.Id, It.IsAny<LlmUsage>(), conversation.Id), Times.Once);
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
        Assert.Contains("pasar la conversación con una persona", system, StringComparison.Ordinal);
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

        Assert.Equal(AgentReplyDecision.ModelProducedNoText, outcome.Decision);
        _runs.Verify(r => r.Record(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmUsage>(), It.IsAny<Guid?>()), Times.Once);
        Assert.Equal(1, _unitOfWork.SaveCount);
        _outbound.Verify(
            o => o.SendAgentReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
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
