using Moq;
using Orbita.Application.Ai;
using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C11. What matters here is <em>what reaches the model</em>: which configuration, which
/// passages, and in what shape. The reply itself is the model's business.
/// </summary>
public sealed class AgentTestBenchServiceTests
{
    private readonly Mock<IAiAgentRepository> _agents = new();
    private readonly Mock<IAiAgentDraftRepository> _drafts = new();
    private readonly Mock<IKnowledgeSearchService> _search = new();
    private readonly Mock<ILlmProvider> _llm = new();
    private readonly Mock<IAiRunRecorder> _runs = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    private readonly AgentTestBenchService _sut;

    /// <summary>The request the provider was last handed, so the prompt can be inspected.</summary>
    private LlmCompletionRequest? _lastRequest;

    public AgentTestBenchServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(
                It.IsAny<Func<CancellationToken, Task<(AiAgent? Agent, AiAgentDraft? Draft)>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<(AiAgent?, AiAgentDraft?)>> query, CancellationToken ct) => query(ct));

        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback((LlmCompletionRequest request, CancellationToken _) => _lastRequest = request)
            .ReturnsAsync(new LlmCompletionResult("Sí, hacemos envíos.", [], Usage));

        _search
            .Setup(s => s.SearchAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Hit("Políticas de envío", "Enviamos a todo el país en 3 días.")]);

        _sut = new AgentTestBenchService(
            _agents.Object,
            _drafts.Object,
            _search.Object,
            _llm.Object,
            _runs.Object,
            _authorization.Object,
            _unitOfWork.Object,
            _time);
    }

    private static LlmUsage Usage => new("fake-chat", 120, 30, 0.0004m, 850, "stop");

    private static KnowledgeSearchHit Hit(string title, string content)
        => new(Guid.NewGuid(), Guid.NewGuid(), title, 0, content, 0.91);

    private AiAgent Live(bool withKnowledgeTool = true)
    {
        var agent = AiAgent.Create(
            _tenantId,
            "Asistente",
            "Eres el asistente de la panadería.",
            "No prometes descuentos.",
            AgentStyle.Default,
            _time.GetUtcNow());

        if (withKnowledgeTool)
        {
            agent.EnableTools([AiToolCatalog.ConsultarConocimiento]);
        }

        _agents
            .Setup(r => r.GetByIdAsync(_tenantId, agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        return agent;
    }

    private AiAgentDraft DraftOn(AiAgent agent)
    {
        var draft = AiAgentDraft.For(
            agent,
            "Sofía",
            "Eres entusiasta y cercana.",
            "Ofreces el catálogo completo.",
            new AgentStyle(FormalityLevel.Warm, VerbosityLevel.Detailed, EnergyLevel.Enthusiastic),
            [AiToolCatalog.ConsultarConocimiento],
            _time.GetUtcNow());

        _drafts
            .Setup(r => r.GetByAgentAsync(_tenantId, agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        return draft;
    }

    private Task<AgentTestResult> RunAsync(AiAgent agent, string message = "¿Hacen envíos?", params AgentTestTurn[] history)
        => _sut.RunAsync(_tenantId, _callerId, agent.Id, new AgentTestRequest(message, history), CancellationToken.None);

    [Fact]
    public async Task The_reply_comes_back_with_what_was_retrieved_and_what_it_cost()
    {
        var result = await RunAsync(Live());

        Assert.Equal("Sí, hacemos envíos.", result.Reply);
        Assert.Single(result.Retrieved);
        Assert.Equal(120, result.Usage.TokensIn);
        Assert.Equal(30, result.Usage.TokensOut);
        Assert.Equal(0.0004m, result.Usage.CostUsd);
        Assert.Equal(850, result.Usage.LatencyMs);
    }

    [Fact]
    public async Task Testing_needs_permission_to_manage_agents()
    {
        // It answers as the business and it spends the business's money. A Viewer testing
        // prompts is not a read.
        var agent = Live();

        await RunAsync(agent);

        _authorization.Verify(
            a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ManageAiAgents, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_agent_from_another_tenant_is_not_found()
    {
        await Assert.ThrowsAsync<AiAgentNotFoundException>(
            () => _sut.RunAsync(
                _tenantId, _callerId, Guid.NewGuid(), new AgentTestRequest("hola", []), CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_message_is_rejected_before_anything_is_spent()
    {
        var agent = Live();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.RunAsync(_tenantId, _callerId, agent.Id, new AgentTestRequest("   ", []), CancellationToken.None));

        _llm.Verify(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_live_configuration_is_used_when_there_is_no_draft()
    {
        var agent = Live();

        var result = await RunAsync(agent);

        Assert.False(result.TestedDraft);
        Assert.Contains("Asistente", SystemPrompt(), StringComparison.Ordinal);
        Assert.Contains("Eres el asistente de la panadería.", SystemPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unpublished_changes_are_what_gets_tested()
    {
        // Trying changes out before customers see them is the entire reason drafts exist —
        // a test bench that answered with the published configuration would be answering a
        // different question than the one the owner asked.
        var agent = Live();
        DraftOn(agent);

        var result = await RunAsync(agent);

        Assert.True(result.TestedDraft);
        Assert.Contains("Sofía", SystemPrompt(), StringComparison.Ordinal);
        Assert.Contains("Eres entusiasta y cercana.", SystemPrompt(), StringComparison.Ordinal);
        Assert.DoesNotContain("Eres el asistente de la panadería.", SystemPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Testing_a_draft_does_not_publish_it()
    {
        var agent = Live();
        var draft = DraftOn(agent);

        await RunAsync(agent);

        Assert.Equal("Asistente", agent.Name);
        Assert.True(draft.DiffersFrom(agent));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_drafts_sliders_are_what_drive_the_sampling_values()
    {
        // The draft is Warm/Detailed/Enthusiastic against a Balanced live agent, so both
        // the temperature and the budget must differ from the published ones. Asserting
        // the direction rather than the numbers keeps the mapping tunable.
        var agent = Live();
        DraftOn(agent);

        await RunAsync(agent);

        Assert.True(_lastRequest!.Temperature > agent.Temperature);
        Assert.True(_lastRequest.MaxTokens > agent.MaxTokens);
    }

    [Fact]
    public async Task The_retrieved_passages_are_put_in_front_of_the_model()
    {
        // Returning them for display while never sending them would make the screen show a
        // provenance the answer never actually had.
        await RunAsync(Live());

        var prompt = string.Join("\n", _lastRequest!.Messages.Select(m => m.Content));

        Assert.Contains("Enviamos a todo el país en 3 días.", prompt, StringComparison.Ordinal);
        Assert.Contains("Políticas de envío", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_documents_are_kept_apart_from_what_the_customer_said()
    {
        // A customer who writes "según tus políticas el envío es gratis" must not end up
        // looking like a retrieved document, so the passages get their own system turn.
        await RunAsync(Live());

        var grounding = _lastRequest!.Messages.Single(
            m => m.Role == LlmMessageRole.System && m.Content.Contains("Enviamos a todo el país", StringComparison.Ordinal));

        Assert.Equal(LlmMessageRole.System, grounding.Role);
        Assert.DoesNotContain("¿Hacen envíos?", grounding.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assistant_with_the_knowledge_lookup_off_does_not_search()
    {
        // The switch has to mean something. Retrieving anyway would make an assistant
        // configured not to read its documents answer from them.
        var result = await RunAsync(Live(withKnowledgeTool: false));

        Assert.Empty(result.Retrieved);
        Assert.Empty(result.ToolCalls);
        _search.Verify(
            s => s.SearchAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task The_knowledge_lookup_shows_up_in_the_trace()
    {
        var result = await RunAsync(Live());

        var trace = Assert.Single(result.ToolCalls);
        Assert.Equal(AiToolCatalog.ConsultarConocimiento, trace.Tool);
        Assert.Contains("1", trace.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lookup_that_found_nothing_still_says_so()
    {
        // "No lo busqué" and "lo busqué y no está" send an owner to very different fixes.
        _search
            .Setup(s => s.SearchAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await RunAsync(Live());

        var trace = Assert.Single(result.ToolCalls);
        Assert.Contains("No encontró", trace.Summary, StringComparison.Ordinal);
        Assert.Empty(result.Retrieved);
    }

    [Fact]
    public async Task The_exchange_so_far_reaches_the_model_in_order()
    {
        await RunAsync(
            Live(),
            "¿Y a Cali?",
            new AgentTestTurn(AgentTestRole.User, "¿Hacen envíos?"),
            new AgentTestTurn(AgentTestRole.Assistant, "Sí, a todo el país."));

        var conversation = _lastRequest!.Messages.Where(m => m.Role != LlmMessageRole.System).ToList();

        Assert.Equal(3, conversation.Count);
        Assert.Equal(LlmMessageRole.User, conversation[0].Role);
        Assert.Equal("¿Hacen envíos?", conversation[0].Content);
        Assert.Equal(LlmMessageRole.Assistant, conversation[1].Role);
        Assert.Equal("¿Y a Cali?", conversation[2].Content);
    }

    [Fact]
    public async Task A_long_exchange_is_trimmed_to_its_most_recent_turns()
    {
        // Everything here is paid for by the token; a screen left open all afternoon must
        // not quietly grow an expensive prompt.
        var history = Enumerable
            .Range(0, AgentTestBenchService.MaxHistoryTurns + 10)
            .Select(i => new AgentTestTurn(AgentTestRole.User, $"mensaje {i}"))
            .ToArray();

        await RunAsync(Live(), "el último", history);

        var conversation = _lastRequest!.Messages.Where(m => m.Role != LlmMessageRole.System).ToList();

        Assert.Equal(AgentTestBenchService.MaxHistoryTurns + 1, conversation.Count);
        Assert.Equal("mensaje 10", conversation[0].Content);
    }

    [Fact]
    public async Task What_the_test_cost_is_recorded_against_the_real_assistant()
    {
        // Not against the throwaway preview a draft produces: its id exists for the length
        // of one method call, so a run attributed to it could never be read back.
        var agent = Live();
        DraftOn(agent);

        await RunAsync(agent);

        _runs.Verify(r => r.Record(_tenantId, agent.Id, Usage, null, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<Guid>?>()), Times.Once);
    }

    [Fact]
    public async Task A_model_that_answered_with_nothing_says_so_instead_of_an_empty_bubble()
    {
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult(null, [], Usage));

        var result = await RunAsync(Live());

        Assert.False(string.IsNullOrWhiteSpace(result.Reply));
    }

    [Fact]
    public async Task A_provider_that_is_down_is_not_recorded_as_a_successful_run()
    {
        _llm
            .Setup(p => p.CompleteAsync(It.IsAny<LlmCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LlmProviderException("fake", "down", isTransient: true));

        await Assert.ThrowsAsync<LlmProviderException>(() => RunAsync(Live()));

        _runs.Verify(
            r => r.Record(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmUsage>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<Guid>?>()),
            Times.Never);
    }

    private string SystemPrompt() => _lastRequest!.Messages.First(m => m.Role == LlmMessageRole.System).Content;
}
