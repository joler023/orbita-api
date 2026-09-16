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
    IConversationHandoffService handoffService,
    IRoutingRuleRepository routingRules,
    Orbita.Domain.Channels.IChannelAccountRepository channelAccounts,
    Orbita.Domain.Tenants.ITenantRepository tenants,
    IAgentAnswerCache answerCache,
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

        // ORB-C07, before the guardrails and before anything is paid for. Asking for a
        // person is the least ambiguous thing a customer can say, and it is checked first
        // so that it also wins over a blocked topic in the same message: both end the
        // assistant's turn, but "me pidieron un humano" is the label the person picking
        // the conversation up can act on, and "parecía molesto" is not.
        if (HandoffTriggers.Detect(incoming, context.GuardrailHistory) is { } trigger)
        {
            return await HandOffAsync(tenantId, conversationId, agent, trigger, agent.HandoffReply, cancellationToken);
        }

        // ORB-C06, before anything is paid for: an out-of-scope subject, a conversation
        // that has had enough answers for one window, or a loop all end here.
        var incomingVerdict = AgentGuardrails.InspectIncoming(
            agent, incoming, context.RepliesInWindow, context.GuardrailHistory);

        if (!incomingVerdict.IsAllowed)
        {
            return await BlockedAsync(tenantId, conversationId, agent, incomingVerdict, cancellationToken);
        }

        // ORB-C12. Only a conversation's first answer is ever reused or stored: anything
        // later was shaped by the turns before it, and replaying it to a different customer
        // would answer a conversation they never had.
        AgentCacheLookup? cacheLookup = null;

        if (agent.SemanticCacheThreshold is not null && context.History.Count == 0)
        {
            cacheLookup = await answerCache.LookupAsync(tenantId, agent, conversationId, incoming, cancellationToken);

            if (cacheLookup.Hit is { } hit
                && AgentGuardrails.InspectReply(hit.Answer, context.GuardrailHistory).IsAllowed)
            {
                var cached = await outboundMessages.SendAgentReplyAsync(
                    tenantId, conversationId, hit.Answer, cacheLookup.RunId, cancellationToken);

                logger.LogInformation(
                    "Assistant {AgentId} reused cache entry {EntryId} (similarity {Score:0.000}) on conversation {ConversationId}.",
                    agent.Id, hit.EntryId, hit.Score, conversationId);

                return AgentReplyOutcome.Replied(cached.Id);
            }
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
        var anyToolCalled = false;

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

            anyToolCalled = true;

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
            // The run is owed to the ledger — the call was made and it cost money — and
            // HandOffAsync's own save is what commits it.
            logger.LogWarning("Model produced no text for conversation {ConversationId}.", conversationId);

            // The customer wrote and got nothing back, which is the círculo ORB-C07 is
            // written against — so it goes to a person instead of being dropped. Silently
            // for the customer: whatever we said now would be an apology for a failure
            // they have not seen yet.
            return await HandOffAsync(
                tenantId, conversationId, agent, HandoffReason.AgentDecision, reply: null, cancellationToken);
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

            // Same as no text at all, from where the customer is standing: they asked
            // something and nothing came back (ORB-C07).
            await handoffService.RequestAsync(
                tenantId,
                conversationId,
                agent.Id,
                HandoffTriggers.ForGuardrail(replyVerdict.Reason!.Value),
                summary: null,
                cancellationToken);

            return AgentReplyOutcome.Blocked(replyVerdict.Reason!.Value);
        }

        // An answer that called a tool did something for this customer — registered their
        // opportunity, moved their stage. Replaying its words to someone else would claim
        // an action that never happened for them.
        if (cacheLookup is not null && !anyToolCalled)
        {
            answerCache.Store(tenantId, agent, cacheLookup, incoming, replyText);
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

        // Every block now also hands the conversation to a person (ORB-C07). Before it,
        // all three outcomes left the customer with the assistant that had just decided
        // not to help them, which is the "círculo" the story is written against. What
        // still differs is what the customer hears, and that has not changed.
        var handoffReason = HandoffTriggers.ForGuardrail(verdict.Reason!.Value);

        if (verdict.Reason != GuardrailReason.OutOfScopeTopic)
        {
            // A loop or an exhausted window gets silence: both mean the assistant has
            // already said too much, so one more sentence is the failure repeating itself.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await handoffService.RequestAsync(tenantId, conversationId, agent.Id, handoffReason, summary: null, cancellationToken);

            return AgentReplyOutcome.Blocked(verdict.Reason!.Value);
        }

        // No ai_run for the sentence: nothing was generated, so there is nothing to bill
        // or to trace. The sentence is the owner's, not the model's, and
        // Message.OutboundAgentText requires a run — which is why this goes out as a
        // system message instead. (The handoff's own summary does cost one call, once.)
        await outboundMessages.SendSystemReplyAsync(
            tenantId, conversationId, agent.OutOfScopeReply, cancellationToken);
        await handoffService.RequestAsync(tenantId, conversationId, agent.Id, handoffReason, summary: null, cancellationToken);

        return AgentReplyOutcome.Blocked(verdict.Reason!.Value);
    }

    /// <summary>
    /// Hands the conversation to a person and, when there is something worth saying, says
    /// it (ORB-C07).
    ///
    /// The sentence is the owner's <see cref="AiAgent.HandoffReply"/> rather than a
    /// generated one, for the same reason as the out-of-scope line: it is sent on paths
    /// where no model call is made, so there is nothing to generate it with — and the
    /// owner's own words beat ours. When the assistant hands over through
    /// <c>escalar_a_humano</c> instead, it says goodbye in its own words, which do mirror
    /// the customer's language.
    /// </summary>
    private async Task<AgentReplyOutcome> HandOffAsync(
        Guid tenantId,
        Guid conversationId,
        AiAgent agent,
        HandoffReason reason,
        string? reply,
        CancellationToken cancellationToken)
    {
        // Saved first so the assignment made while loading is not lost if the handoff
        // itself fails halfway.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var handedOver = await handoffService.RequestAsync(tenantId, conversationId, agent.Id, reason, summary: null, cancellationToken);

        // Only when this call is the one that moved it. A customer who writes three times
        // while already queued should not be told three times.
        if (handedOver && !string.IsNullOrWhiteSpace(reply))
        {
            await outboundMessages.SendSystemReplyAsync(tenantId, conversationId, reply, cancellationToken);
        }

        return AgentReplyOutcome.HandedOff(reason);
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

            // Two ways a person owns this conversation: ORB-C07 handed it over and nobody
            // has given it back, or ORB-B15 assigned it to someone. Talking over either is
            // the one failure mode a customer never forgives — and for the handoff it is
            // also the last acceptance criterion of C07, which is why the flag lives on
            // the conversation instead of being inferred from its status.
            if (conversation.IsWaitingForHuman || conversation.AssigneeId is not null)
            {
                return ReplyContext.Skip(AgentReplyDecision.HumanIsHandlingIt);
            }

            if (!conversation.CanSendFreeForm(timeProvider.GetUtcNow()))
            {
                return ReplyContext.Skip(AgentReplyDecision.ServiceWindowClosed);
            }

            // ORB-C08. A conversation keeps whichever assistant took it; a new one is
            // routed by the tenant's ordered rules, and with no matching rule it falls back
            // to the tenant's enabled assistant — exactly the behaviour before routing, so
            // a tenant that never configures rules sees no change.
            var channel = (await channelAccounts.GetByIdAsync(conversation.ChannelAccountId, ct))?.Kind
                ?? Orbita.Domain.Channels.ChannelKind.WhatsApp;
            var decision = RoutingPolicy.Decide(
                await routingRules.ListByTenantAsync(tenantId, ct), conversation.AiAgentId, channel, message.Body);

            if (decision.LeaveForTeam)
            {
                return ReplyContext.Skip(AgentReplyDecision.LeftForTeamByRule);
            }

            var agent = decision.AgentId is { } routed
                ? await agents.GetByIdAsync(tenantId, routed, ct)
                : await agents.FindEnabledByTenantAsync(tenantId, ct);

            if (agent is null)
            {
                return ReplyContext.Skip(AgentReplyDecision.NoAgentAssigned);
            }

            if (!agent.IsEnabled)
            {
                return ReplyContext.Skip(AgentReplyDecision.AgentDisabled);
            }

            // Outside its hours, an assistant configured to leave it for the team stays
            // quiet — checked before the assignment, so a conversation that arrives at 2am
            // is not claimed by an assistant that then never answers it.
            if (agent.BusinessHours is { OutsideHours: OutsideHoursBehavior.LeaveForTeam } hours)
            {
                var tenant = await tenants.GetByIdAsync(tenantId, ct);

                if (!hours.IsOpenAt(timeProvider.GetUtcNow(), ResolveTimeZone(tenant?.Timezone)))
                {
                    return ReplyContext.Skip(AgentReplyDecision.OutsideBusinessHours);
                }
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

    /// <summary>
    /// The tenant's IANA zone (e.g. <c>America/Bogota</c>). An unknown id falls back to UTC
    /// rather than failing the reply: a mistyped zone should shift the hours, not silence
    /// the assistant.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;
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
