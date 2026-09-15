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
    Orbita.Application.Outbox.IOutboxWriter outboxWriter,
    IAgentToolExecutor toolExecutor,
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

    /// <summary>
    /// How many rounds of tool calls one reply may take (ORB-C06's "límite de
    /// iteraciones"). Past it, the model is asked once more with no tools offered, so the
    /// customer always gets an answer instead of an assistant stuck calling tools — and the
    /// cost of one reply stays bounded at <c>MaxToolRounds + 1</c> model calls.
    /// </summary>
    public const int MaxToolRounds = 3;

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

        // ORB-C06, before anything is paid for: an out-of-scope subject, a conversation
        // that has had enough answers for one window, or a loop all end here.
        var incomingVerdict = AgentGuardrails.InspectIncoming(
            agent, incoming, context.RepliesInWindow, context.GuardrailHistory);

        if (!incomingVerdict.IsAllowed)
        {
            return await BlockedAsync(tenantId, conversationId, agent, incomingVerdict, cancellationToken);
        }

        var retrieved = await RetrieveAsync(tenantId, agent, incoming, cancellationToken);

        var messages = AgentPromptBuilder
            .Build(agent, retrieved, context.History, incoming, includeConversationRules: true)
            .ToList();
        var tools = toolExecutor.DefinitionsFor(agent);
        var toolContext = new AgentToolContext(tenantId, agent.Id, context.ContactId, conversationId);

        LlmCompletionResult completion;
        Guid runId;
        var round = 0;

        while (true)
        {
            var offerTools = tools.Count > 0 && round < MaxToolRounds;

            completion = await llmProvider.CompleteAsync(
                new LlmCompletionRequest(
                    tenantId,
                    LlmTask.Draft,
                    messages,
                    agent.Temperature,
                    agent.MaxTokens,
                    offerTools ? tools : null),
                cancellationToken);

            // Every round is its own paid call and its own run. Staged, not saved:
            // SendAgentReplyAsync's SaveChangesAsync commits the runs, the reply and the
            // queued job together.
            runId = runRecorder.Record(
                tenantId,
                agent.Id,
                completion.Usage,
                conversationId,
                toolsCalled: [.. completion.ToolCalls.Select(call => call.Name)],
                // The passages went into every round's prompt, but they were retrieved
                // once; recording them on the first run only keeps a sum over runs honest.
                retrievedChunkIds: round == 0 ? [.. retrieved.Select(hit => hit.ChunkId)] : null);

            if (!offerTools || completion.ToolCalls.Count == 0)
            {
                break;
            }

            // The assistant turn that asked for the tools goes back first — the chat format
            // rejects a tool result that does not answer a call from the turn before it.
            messages.Add(LlmMessage.AssistantToolCalls(completion.ToolCalls));

            foreach (var call in completion.ToolCalls)
            {
                var result = await toolExecutor.ExecuteAsync(toolContext, call, cancellationToken);
                messages.Add(LlmMessage.Tool(call.Id, result.ResultJson));

                logger.LogInformation(
                    "Assistant {AgentId} called {Tool} on conversation {ConversationId}: {Outcome}.",
                    agent.Id, call.Name, conversationId, result.Succeeded ? "ok" : "failed");
            }

            round++;
        }

        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            // Still save: the call was made and it cost money, so it is owed to the ledger
            // whether or not it produced anything worth sending.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Model produced no text for conversation {ConversationId}.", conversationId);

            return AgentReplyOutcome.Skipped(AgentReplyDecision.ModelProducedNoText);
        }

        var replyText = completion.Content.Trim();
        var replyVerdict = AgentGuardrails.InspectReply(replyText, context.GuardrailHistory);

        if (!replyVerdict.IsAllowed)
        {
            // The run is still owed to the ledger: the call was made and it cost money,
            // whatever we decided to do with what came back.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Guardrail {Reason} blocked the reply on conversation {ConversationId}.",
                replyVerdict.Reason, conversationId);

            return AgentReplyOutcome.Blocked(replyVerdict.Reason!.Value);
        }

        var reply = await outboundMessages.SendAgentReplyAsync(
            tenantId, conversationId, replyText, runId, cancellationToken);

        logger.LogInformation(
            "Assistant {AgentId} answered conversation {ConversationId} in {LatencyMs}ms.",
            agent.Id, conversationId, completion.Usage.LatencyMs);

        return AgentReplyOutcome.Replied(reply.Id);
    }

    /// <summary>
    /// What a blocked message actually does to the conversation.
    ///
    /// An out-of-scope subject gets an answer — a short, fixed sentence handing the
    /// customer to a person. Silence would be the worst outcome of the three: the
    /// customer asked something the business deliberately does not let a machine answer,
    /// so leaving them with nothing is exactly the "círculo" ORB-C07 is written against.
    /// It costs no model call, which is why the check runs before one.
    ///
    /// A loop or an exhausted window gets silence instead, and deliberately: both mean
    /// the assistant has already said too much, so saying one more thing — even an
    /// apology — is the failure repeating itself one more time.
    /// </summary>
    private async Task<AgentReplyOutcome> BlockedAsync(
        Guid tenantId,
        Guid conversationId,
        AiAgent agent,
        GuardrailVerdict verdict,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Guardrail {Reason} stopped assistant {AgentId} on conversation {ConversationId}.",
            verdict.Reason, agent.Id, conversationId);

        // Every block leaves a record, not just the interesting one. A silenced assistant
        // is the hardest thing in this product to diagnose from the outside — it does not
        // error, it just stops answering — so "why did mine go quiet?" needs something to
        // read in all three cases.
        //
        // `topic` is the deliberate exception to the outbox's ids-and-enums rule: it is
        // free text the owner wrote. It is here because the answer to that question is the
        // word itself, and an index into a list that changes is useless a week later. It
        // is the business's own configuration, not a customer's data.
        await outboxWriter.StageAsync(
            tenantId,
            nameof(AiAgent),
            agent.Id,
            "agent.reply_blocked",
            new { conversationId, agentId = agent.Id, reason = verdict.Reason.ToString(), topic = verdict.MatchedTopic },
            cancellationToken);

        if (verdict.Reason != GuardrailReason.OutOfScopeTopic)
        {
            // A loop or an exhausted window gets silence: both mean the assistant has
            // already said too much, so one more sentence is the failure repeating itself.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return AgentReplyOutcome.Blocked(verdict.Reason!.Value);
        }

        // No ai_run: nothing was generated, so there is nothing to bill or to trace. The
        // sentence is the owner's, not the model's, and Message.OutboundAgentText requires
        // a run — which is why this goes out as a system message instead.
        await outboundMessages.SendSystemReplyAsync(
            tenantId, conversationId, agent.OutOfScopeReply, cancellationToken);

        return AgentReplyOutcome.Blocked(verdict.Reason!.Value);
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

            if (conversation is null)
            {
                return ReplyContext.Skip(AgentReplyDecision.ConversationGone);
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

            // A conversation keeps whichever assistant first answered it; one that has
            // none yet gets the tenant's enabled assistant, and records that choice.
            var agent = conversation.AiAgentId is { } assigned
                ? await agents.GetByIdAsync(tenantId, assigned, ct)
                : await agents.FindEnabledByTenantAsync(tenantId, ct);

            if (agent is null)
            {
                return ReplyContext.Skip(AgentReplyDecision.NoAgentAssigned);
            }

            if (!agent.IsEnabled)
            {
                return ReplyContext.Skip(AgentReplyDecision.AgentDisabled);
            }

            // Tracked by the same DbContext the reply is saved through, so the assignment
            // and the reply commit together or not at all.
            conversation.AssignAgent(agent.Id);

            var history = await messages.GetRecentByConversationAsync(
                tenantId, conversationId, MaxHistoryTurns + 1, ct);

            // Counted over the current service window, which is the unit the limit is
            // about: "how much has this assistant already said in this exchange".
            var windowStart = timeProvider.GetUtcNow() - Conversation.ServiceWindow;
            var repliesInWindow = await messages.CountAgentRepliesSinceAsync(
                tenantId, conversationId, windowStart, ct);

            return new ReplyContext(
                agent,
                message.Body,
                // The message being answered is the last turn of the prompt, not part of
                // the history, so it must not appear twice.
                [.. history
                    .Where(m => m.Id != message.Id && !string.IsNullOrWhiteSpace(m.Body))
                    .TakeLast(MaxHistoryTurns)
                    .Select(m => new AgentTurn(m.Direction == MessageDirection.Outbound, m.Body!))],
                repliesInWindow,
                conversation.ContactId,
                SkipReason: null);
        },
        cancellationToken);
    }

    private sealed record ReplyContext(
        AiAgent? Agent,
        string? Incoming,
        IReadOnlyList<AgentTurn> History,
        int RepliesInWindow,
        Guid ContactId,
        AgentReplyDecision? SkipReason)
    {
        public static ReplyContext Skip(AgentReplyDecision decision) => new(null, null, [], 0, Guid.Empty, decision);

        /// <summary>The same turns, in the shape the domain guardrails judge.</summary>
        public IReadOnlyList<AgentConversationTurn> GuardrailHistory
            => [.. History.Select(turn => new AgentConversationTurn(turn.FromAssistant, turn.Content))];
    }
}
