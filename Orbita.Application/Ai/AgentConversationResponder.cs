using Microsoft.Extensions.Logging;
using Orbita.Application.Inbox;
using Orbita.Domain.Ai;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Ai;

public sealed class AgentConversationResponder(
    IConversationRepository conversations,
    IMessageRepository messages,
    IAiAgentRepository agents,
    IKnowledgeSearchService knowledgeSearch,
    ILlmProvider llmProvider,
    IAiRunRecorder runRecorder,
    IOutboundMessageService outboundMessages,
    Orbita.Domain.Common.IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<AgentConversationResponder> logger) : IAgentConversationResponder
{
    /// <summary>
    /// How many earlier turns are carried into the prompt. Same bound as the test bench,
    /// and for the same reason it is a bound at all: a conversation that has run for
    /// months would otherwise grow its own prompt without limit, and the cost of every
    /// future reply with it.
    /// </summary>
    public const int MaxHistoryTurns = 20;

    public async Task<AgentReplyOutcome> RespondAsync(
        Guid tenantId,
        Guid conversationId,
        Guid inboundMessageId,
        CancellationToken cancellationToken)
    {
        var context = await LoadAsync(tenantId, conversationId, inboundMessageId, cancellationToken);

        if (context.SkipReason is { } skip)
        {
            logger.LogDebug(
                "Assistant stayed quiet on conversation {ConversationId}: {Decision}.",
                conversationId, skip);

            return AgentReplyOutcome.Skipped(skip);
        }

        var agent = context.Agent!;
        var incoming = context.Incoming!;

        var retrieved = await RetrieveAsync(tenantId, agent, incoming, cancellationToken);

        var completion = await llmProvider.CompleteAsync(
            new LlmCompletionRequest(
                tenantId,
                LlmTask.Draft,
                AgentPromptBuilder.Build(agent, retrieved, context.History, incoming, includeConversationRules: true),
                agent.Temperature,
                agent.MaxTokens),
            cancellationToken);

        // Staged, not saved: SendAgentReplyAsync's own SaveChangesAsync is what commits
        // this run, the reply and the queued job together. A run that outlived a failed
        // send would bill for an answer the customer never got.
        var runId = runRecorder.Record(tenantId, agent.Id, completion.Usage, conversationId);

        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            // Still save: the call was made and it cost money, so it is owed to the ledger
            // whether or not it produced anything worth sending.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Model produced no text for conversation {ConversationId}.", conversationId);

            return AgentReplyOutcome.Skipped(AgentReplyDecision.ModelProducedNoText);
        }

        var reply = await outboundMessages.SendAgentReplyAsync(
            tenantId, conversationId, completion.Content.Trim(), runId, cancellationToken);

        logger.LogInformation(
            "Assistant {AgentId} answered conversation {ConversationId} in {LatencyMs}ms.",
            agent.Id, conversationId, completion.Usage.LatencyMs);

        return AgentReplyOutcome.Replied(reply.Id);
    }

    /// <summary>
    /// Retrieval is a tool like any other: an assistant whose <c>consultar_conocimiento</c>
    /// is switched off must not quietly get grounding anyway, or the switch is a lie.
    /// Same rule the test bench applies, so what the owner tested is what the customer gets.
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeSearchHit>> RetrieveAsync(
        Guid tenantId,
        AiAgent agent,
        string question,
        CancellationToken cancellationToken)
    {
        if (!agent.Tools.Contains(AiToolCatalog.ConsultarConocimiento, StringComparer.Ordinal))
        {
            return [];
        }

        return await knowledgeSearch.SearchForAgentAsync(
            tenantId, agent.Id, question, KnowledgeSearchService.DefaultLimit, cancellationToken);
    }

    /// <summary>
    /// One tenant-scoped read for everything the decision needs. Grouped into a single
    /// <c>QueryInTenantScopeAsync</c> rather than four, because each one opens its own
    /// transaction to set <c>app.tenant_id</c> and the assistant's latency budget is six
    /// seconds at p95 — round trips to Postgres are the cheapest part to not spend.
    /// </summary>
    private async Task<ReplyContext> LoadAsync(
        Guid tenantId,
        Guid conversationId,
        Guid inboundMessageId,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.QueryInTenantScopeAsync<ReplyContext>(async ct =>
        {
            var message = await messages.GetByIdAsync(inboundMessageId, ct);

            if (message is null || message.TenantId != tenantId || message.Direction != MessageDirection.Inbound)
            {
                return ReplyContext.Skip(AgentReplyDecision.NotAnInboundMessage);
            }

            // An image with no caption, a sticker, a location: there is nothing to read,
            // and answering "no entendí" to every photo a customer sends is worse than
            // letting a person see it.
            if (string.IsNullOrWhiteSpace(message.Body))
            {
                return ReplyContext.Skip(AgentReplyDecision.NothingToAnswer);
            }

            var conversation = await conversations.GetByIdAsync(conversationId, ct);

            if (conversation?.AiAgentId is not { } agentId)
            {
                return ReplyContext.Skip(AgentReplyDecision.NoAgentAssigned);
            }

            // ORB-C07 is what makes a handoff explicit and permanent. Until it exists,
            // "somebody is already assigned" is the closest honest signal that a person
            // took this conversation, and talking over them is the one failure mode a
            // customer never forgives.
            if (conversation.AssigneeId is not null)
            {
                return ReplyContext.Skip(AgentReplyDecision.HumanIsHandlingIt);
            }

            if (!conversation.CanSendFreeForm(timeProvider.GetUtcNow()))
            {
                return ReplyContext.Skip(AgentReplyDecision.ServiceWindowClosed);
            }

            var agent = await agents.GetByIdAsync(tenantId, agentId, ct);

            if (agent is null)
            {
                return ReplyContext.Skip(AgentReplyDecision.NoAgentAssigned);
            }

            if (!agent.IsEnabled)
            {
                return ReplyContext.Skip(AgentReplyDecision.AgentDisabled);
            }

            var history = await messages.GetRecentByConversationAsync(
                tenantId, conversationId, MaxHistoryTurns + 1, ct);

            return new ReplyContext(
                agent,
                message.Body,
                // The message being answered is the last turn of the prompt, not part of
                // the history, so it must not appear twice.
                [.. history
                    .Where(m => m.Id != message.Id && !string.IsNullOrWhiteSpace(m.Body))
                    .TakeLast(MaxHistoryTurns)
                    .Select(m => new AgentTurn(m.Direction == MessageDirection.Outbound, m.Body!))],
                SkipReason: null);
        },
        cancellationToken);
    }

    private sealed record ReplyContext(
        AiAgent? Agent,
        string? Incoming,
        IReadOnlyList<AgentTurn> History,
        AgentReplyDecision? SkipReason)
    {
        public static ReplyContext Skip(AgentReplyDecision decision) => new(null, null, [], decision);
    }
}
