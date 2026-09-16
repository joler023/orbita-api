using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Ai;
using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Application.Outbox;
using Orbita.Domain.Ai;
using Orbita.Domain.Audit;
using Orbita.Domain.Inbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C07's service. The interesting cases are all about what must still happen when
/// something goes wrong: a customer who asked for a person has to reach the queue even if
/// the model that writes the summary is down, and a customer who keeps writing while
/// queued must not be handed over again and again.
/// </summary>
public sealed class ConversationHandoffServiceTests
{
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<ILlmProvider> _llm = new();
    private readonly Mock<IAiRunRecorder> _runs = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly PassThroughUnitOfWork _unitOfWork = new();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 9, 16, 15, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _agentId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    private readonly ConversationHandoffService _sut;

    public ConversationHandoffServiceTests()
    {
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult(
                "El cliente pregunta por una devolución fuera de plazo.",
                [],
                new LlmUsage("deepseek/deepseek-v4-flash", 300, 40, 0.00002m, 400, "stop")));

        _sut = new ConversationHandoffService(
            _conversations.Object,
            _messages.Object,
            _llm.Object,
            _runs.Object,
            _outbox.Object,
            _audit.Object,
            _authorization.Object,
            _unitOfWork,
            _time,
            NullLogger<ConversationHandoffService>.Instance);
    }

    private Conversation Conversation(bool alreadyWaiting = false)
    {
        var conversation = Orbita.Domain.Inbox.Conversation.Open(_tenantId, Guid.NewGuid(), Guid.NewGuid(), _time.GetUtcNow());
        conversation.RegisterInbound(_time.GetUtcNow(), "hola");

        if (alreadyWaiting)
        {
            conversation.RequestHumanHandoff(HandoffReason.CustomerAsked, "Ya estaba en cola.", _time.GetUtcNow());
        }

        _conversations.Setup(r => r.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _messages
            .Setup(r => r.GetRecentByConversationAsync(_tenantId, conversation.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Message.Inbound(_tenantId, conversation.Id, "wamid.1", "quiero hablar con una persona", null, null, _time.GetUtcNow())]);

        return conversation;
    }

    [Fact]
    public async Task Handing_over_writes_the_summary_the_event_and_the_audit_entry()
    {
        var conversation = Conversation();

        var handedOver = await _sut.RequestAsync(
            _tenantId, conversation.Id, _agentId, HandoffReason.CustomerAsked, summary: null, CancellationToken.None);

        Assert.True(handedOver);
        Assert.True(conversation.IsWaitingForHuman);
        Assert.Equal("El cliente pregunta por una devolución fuera de plazo.", conversation.HandoffSummary);

        // The run that paid for the summary is the one flagged as the handoff —
        // orbita-schema.dbml's was_handoff, unset until this story existed.
        _runs.Verify(
            r => r.Record(_tenantId, _agentId, It.IsAny<LlmUsage>(), conversation.Id, null, null, true),
            Times.Once);

        _outbox.Verify(
            o => o.StageAsync(
                _tenantId,
                nameof(Orbita.Domain.Inbox.Conversation),
                conversation.Id,
                ConversationHandoffService.HandoffRequestedEventType,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _audit.Verify(
            a => a.RecordSystemActionAsync(
                _tenantId,
                AuditActorType.AiAgent,
                "conversation.handed_off",
                nameof(Orbita.Domain.Inbox.Conversation),
                conversation.Id,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // One transaction for the state, the event and the audit entry.
        Assert.Equal(1, _unitOfWork.TenantScopedSaveCount);
    }

    [Fact]
    public async Task A_customer_who_keeps_writing_while_queued_is_handed_over_once()
    {
        var conversation = Conversation(alreadyWaiting: true);

        var handedOver = await _sut.RequestAsync(
            _tenantId, conversation.Id, _agentId, HandoffReason.Frustration, summary: null, CancellationToken.None);

        Assert.False(handedOver);

        // No second summary, no second event: nothing changed, so nothing is paid for.
        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _outbox.VerifyNoOtherCalls();
        Assert.Equal(0, _unitOfWork.TenantScopedSaveCount);
    }

    [Fact]
    public async Task A_model_outage_does_not_keep_the_customer_from_reaching_a_person()
    {
        var conversation = Conversation();
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LlmProviderException("openrouter", "every provider is down", isTransient: true));

        var handedOver = await _sut.RequestAsync(
            _tenantId, conversation.Id, _agentId, HandoffReason.CustomerAsked, summary: null, CancellationToken.None);

        // The whole point: the handoff is the product's promise, the summary is a
        // convenience. Losing the second must never cost the first.
        Assert.True(handedOver);
        Assert.True(conversation.IsWaitingForHuman);
        Assert.Null(conversation.HandoffSummary);
    }

    [Fact]
    public async Task The_assistants_own_note_is_used_instead_of_paying_for_another_one()
    {
        var conversation = Conversation();

        await _sut.RequestAsync(
            _tenantId,
            conversation.Id,
            _agentId,
            HandoffReason.AgentDecision,
            summary: "  Quiere devolver un pedido de la semana pasada.  ",
            CancellationToken.None);

        Assert.Equal("Quiere devolver un pedido de la semana pasada.", conversation.HandoffSummary);
        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Giving_a_conversation_back_is_audited_as_the_person_who_did_it()
    {
        var conversation = Conversation(alreadyWaiting: true);

        var state = await _sut.ReturnToAssistantAsync(_tenantId, _callerId, conversation.Id, CancellationToken.None);

        Assert.False(state.IsWaitingForHuman);
        Assert.Null(state.Reason);

        _audit.Verify(
            a => a.RecordAsync(
                _tenantId,
                _callerId,
                "conversation.returned_to_assistant",
                nameof(Orbita.Domain.Inbox.Conversation),
                conversation.Id,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Giving_back_a_conversation_nobody_took_is_not_an_event_worth_recording()
    {
        var conversation = Conversation();

        var state = await _sut.ReturnToAssistantAsync(_tenantId, _callerId, conversation.Id, CancellationToken.None);

        Assert.False(state.IsWaitingForHuman);
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_conversation_from_another_tenant_is_not_found()
    {
        var conversation = Conversation();

        await Assert.ThrowsAsync<ConversationNotFoundException>(() => _sut.ReturnToAssistantAsync(
            Guid.NewGuid(), _callerId, conversation.Id, CancellationToken.None));
    }
}
