using System.Text;
using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

public sealed class AgentTestBenchService(
    IAiAgentRepository agentRepository,
    IAiAgentDraftRepository draftRepository,
    IKnowledgeSearchService knowledgeSearch,
    ILlmProvider llmProvider,
    IAiRunRecorder runRecorder,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAgentTestBenchService
{
    /// <summary>
    /// How many earlier turns are carried into the prompt. Enough to test a follow-up
    /// question, capped because everything here is paid for by the token and an operator
    /// who leaves the screen open all afternoon should not accumulate an expensive prompt.
    /// </summary>
    public const int MaxHistoryTurns = 20;

    public const int MaxMessageLength = 2_000;

    public async Task<AgentTestResult> RunAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        AgentTestRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);

        if (request.Message.Length > MaxMessageLength)
        {
            throw new ArgumentException($"Must be at most {MaxMessageLength} characters.", nameof(request));
        }

        var (agent, draft) = await LoadAsync(tenantId, agentId, cancellationToken);

        // Unpublished changes win: testing what you just wrote, before customers get it, is
        // the whole reason drafts exist.
        var configuration = draft?.AsUnsavedPreview(timeProvider.GetUtcNow()) ?? agent;

        var (retrieved, traces) = await RetrieveAsync(
            tenantId, callerUserId, agentId, configuration, request.Message, cancellationToken);

        var completion = await llmProvider.CompleteAsync(
            new LlmCompletionRequest(
                tenantId,
                LlmTask.Draft,
                AgentPromptBuilder.Build(
                    configuration,
                    retrieved,
                    (request.History ?? [])
                        .TakeLast(MaxHistoryTurns)
                        .Select(turn => new AgentTurn(turn.Role == AgentTestRole.Assistant, turn.Content)),
                    request.Message,
                    // The test bench predicts the live agent, so it gets the same standing
                    // rules ORB-C04 sends — an owner testing without them would be testing
                    // a different assistant than the one their customers meet.
                    includeConversationRules: true),
                configuration.Temperature,
                configuration.MaxTokens),
            cancellationToken);

        // Attributed to the real assistant, never to the throwaway preview a draft produces
        // — its id exists only inside this method. No conversation id: there is no
        // conversation, which is exactly why this is not blocked on Track B.
        runRecorder.Record(tenantId, agent.Id, completion.Usage);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AgentTestResult(
            // A model that answers with nothing but a tool call is normal, not a failure,
            // but there is nothing to show the operator either — say so rather than
            // rendering an empty bubble.
            string.IsNullOrWhiteSpace(completion.Content)
                ? "El asistente no produjo una respuesta."
                : completion.Content,
            retrieved,
            traces,
            Total(completion),
            TestedDraft: draft is not null);
    }

    /// <summary>
    /// Reuses ORB-C03's search rather than embedding and querying again here. It costs one
    /// extra transaction and one extra permission check, and buys not having two places
    /// that decide how a question becomes a vector — a divergence there would not fail a
    /// test, it would just quietly make the test bench answer differently from the real
    /// agent.
    /// </summary>
    private async Task<(IReadOnlyList<KnowledgeSearchHit> Hits, IReadOnlyList<AgentToolCallTrace> Traces)> RetrieveAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        AiAgent configuration,
        string message,
        CancellationToken cancellationToken)
    {
        // The lookup is a tool like any other: an assistant with it switched off must not
        // quietly get retrieval anyway, or the switch is a lie.
        if (!configuration.Tools.Contains(AiToolCatalog.ConsultarConocimiento, StringComparer.Ordinal))
        {
            return ([], []);
        }

        var hits = await knowledgeSearch.SearchAsync(
            tenantId, callerUserId, agentId, message, KnowledgeSearchService.DefaultLimit, cancellationToken);

        var summary = hits.Count == 0
            ? "No encontró nada en los documentos del asistente."
            : $"Encontró {hits.Count} fragmento(s) en los documentos del asistente.";

        return (hits, [new AgentToolCallTrace(AiToolCatalog.ConsultarConocimiento, summary)]);
    }

    /// <summary>
    /// The reply's own cost. The question's embedding is billed too, but ORB-C03's search
    /// already recorded it in <c>ai_runs</c> and does not hand its usage back — the ledger
    /// is complete, this number is just the part of it the screen shows.
    /// </summary>
    private static AgentTestUsage Total(LlmCompletionResult completion)
        => new(
            completion.Usage.TokensIn,
            completion.Usage.TokensOut,
            completion.Usage.CostUsd,
            completion.Usage.LatencyMs);

    private async Task<(AiAgent Agent, AiAgentDraft? Draft)> LoadAsync(
        Guid tenantId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var loaded = await unitOfWork.QueryInTenantScopeAsync(
            async ct => (
                Agent: await agentRepository.GetByIdAsync(tenantId, agentId, ct),
                Draft: await draftRepository.GetByAgentAsync(tenantId, agentId, ct)),
            cancellationToken);

        return loaded.Agent is null
            ? throw new AiAgentNotFoundException()
            : (loaded.Agent, loaded.Draft);
    }
}
