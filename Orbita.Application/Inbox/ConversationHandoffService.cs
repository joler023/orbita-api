using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Orbita.Application.Ai;
using Orbita.Application.Audit;
using Orbita.Application.Common;
using Orbita.Application.Identity;
using Orbita.Application.Outbox;
using Orbita.Domain.Ai;
using Orbita.Domain.Audit;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

public sealed class ConversationHandoffService(
    IConversationRepository conversations,
    IMessageRepository messages,
    ILlmProvider llmProvider,
    IAiRunRecorder runRecorder,
    IOutboxWriter outboxWriter,
    IAuditLogger auditLogger,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ConversationHandoffService> logger) : IConversationHandoffService
{
    public const string HandoffRequestedEventType = "conversation.handoff_requested";

    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    /// <summary>How many turns of the exchange are summarized. The same bound the reply itself uses.</summary>
    public const int SummaryHistoryTurns = 20;

    /// <summary>
    /// Enough for three sentences and not enough for a transcript, which is what the
    /// instruction asks for anyway.
    /// </summary>
    private const int SummaryMaxTokens = 220;

    /// <summary>
    /// Written for a teammate, not for the customer: no greeting, no second person, no
    /// invention. Tuteo like every other Spanish string this backend produces — a prompt
    /// in voseo teaches the model to answer in voseo (see CLAUDE.md).
    /// </summary>
    private const string SummaryInstructions =
        "Resume para una persona del equipo que va a retomar esta conversación.\n"
        + "- Máximo tres frases, en español, en tuteo.\n"
        + "- Di qué necesita el cliente y en qué quedó la conversación.\n"
        + "- No saludes, no te dirijas al cliente y no inventes datos que no estén en el texto.";

    public async Task<bool> RequestAsync(
        Guid tenantId,
        Guid conversationId,
        Guid agentId,
        HandoffReason reason,
        string? summary,
        CancellationToken cancellationToken)
    {
        var context = await unitOfWork.QueryInTenantScopeAsync(
            async ct =>
            {
                var conversation = await conversations.GetByIdAsync(conversationId, ct);

                if (conversation is null || conversation.TenantId != tenantId || conversation.IsWaitingForHuman)
                {
                    return null;
                }

                var history = await messages.GetRecentByConversationAsync(
                    tenantId, conversationId, SummaryHistoryTurns, ct);

                return new HandoffContext(conversation.ContactId, Transcript(history));
            },
            cancellationToken);

        if (context is null)
        {
            // Already waiting, or gone. Both are ordinary: a customer who writes three
            // times while queued should be handed over once, not three times.
            return false;
        }

        // Outside the transaction on purpose: this is a model call, and holding a Postgres
        // transaction open across it would pin a pooled connection for seconds. Skipped
        // entirely when the assistant already wrote the note itself.
        summary = string.IsNullOrWhiteSpace(summary)
            ? await SummarizeAsync(tenantId, agentId, conversationId, context.Transcript, cancellationToken)
            : summary.Trim();

        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                var conversation = await conversations.GetByIdAsync(conversationId, ct);

                if (conversation is null || conversation.TenantId != tenantId)
                {
                    return;
                }

                conversation.RequestHumanHandoff(reason, summary, timeProvider.GetUtcNow());

                // Ids and the reason only — never the summary. It is what the customer
                // said, and an outbox row is read by handlers with no tenant scoping and
                // can sit unpublished for a while (CLAUDE.md, ORB-B04). The summary lives
                // on the conversation, which is RLS'd and read through this service.
                await outboxWriter.StageAsync(
                    tenantId,
                    nameof(Conversation),
                    conversationId,
                    HandoffRequestedEventType,
                    new
                    {
                        conversationId,
                        contactId = context.ContactId,
                        agentId,
                        reason = reason.ToString(),
                    },
                    ct);

                // actor_type = AiAgent, like ORB-C05's tool actions: nobody authorized
                // this, the assistant decided it, and that is worth writing down.
                await auditLogger.RecordSystemActionAsync(
                    tenantId,
                    AuditActorType.AiAgent,
                    "conversation.handed_off",
                    nameof(Conversation),
                    conversationId,
                    new { agentId, reason = reason.ToString() },
                    ct);
            },
            cancellationToken);

        logger.LogInformation(
            "Conversation {ConversationId} handed to a person ({Reason}) by assistant {AgentId}.",
            conversationId, reason, agentId);

        return true;
    }

    public async Task<HandoffQueuePage> ListWaitingAsync(
        Guid tenantId,
        Guid callerUserId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewInbox, cancellationToken);

        var pageSize = Math.Clamp(limit <= 0 ? DefaultPageSize : limit, 1, MaxPageSize);
        var waitingSince = DecodeCursor(cursor);

        // Its own tenant-scoped transaction: the one EnsurePermissionAsync opened has
        // already committed, and SET LOCAL app.tenant_id does not outlive it.
        var (entries, total) = await unitOfWork.QueryInTenantScopeAsync(
            async ct =>
            (
                await conversations.ListWaitingForHumanAsync(tenantId, waitingSince, pageSize + 1, ct),
                await conversations.CountWaitingForHumanAsync(tenantId, ct)
            ),
            cancellationToken);

        var hasMore = entries.Count > pageSize;
        var page = hasMore ? entries.Take(pageSize).ToList() : entries;

        return new HandoffQueuePage(
            [.. page.Select(HandoffDto.From)],
            hasMore ? EncodeCursor(page[^1].RequestedAt) : null,
            total);
    }

    public async Task<HandoffStateDto> ReturnToAssistantAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        HandoffStateDto? state = null;

        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                var conversation = await conversations.GetByIdAsync(conversationId, ct)
                    ?? throw new ConversationNotFoundException();

                if (conversation.TenantId != tenantId)
                {
                    throw new ConversationNotFoundException();
                }

                var wasWaiting = conversation.IsWaitingForHuman;

                conversation.ReturnToAssistant();

                if (wasWaiting)
                {
                    // A person's decision on the tenant's data, so it is audited as that
                    // person — unlike the handoff itself, which no human asked for.
                    await auditLogger.RecordAsync(
                        tenantId,
                        callerUserId,
                        "conversation.returned_to_assistant",
                        nameof(Conversation),
                        conversationId,
                        diff: null,
                        ct);
                }

                state = HandoffStateDto.From(conversation);
            },
            cancellationToken);

        return state!;
    }

    /// <summary>
    /// The summary, or null when the model could not produce one.
    ///
    /// A failure here must never stop the handoff: the customer asked for a person, and
    /// answering "the summarizer is down" by leaving them with the assistant is the exact
    /// círculo ORB-C07 exists to break. The run is still recorded — the call was made and
    /// it cost time — and the person picking the conversation up reads the messages, which
    /// were always the source anyway.
    /// </summary>
    private async Task<string?> SummarizeAsync(
        Guid tenantId,
        Guid agentId,
        Guid conversationId,
        string transcript,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        try
        {
            // The cheap tier (ORB-C13). A three-sentence internal note is the same
            // economics as a classification, and a task of its own would mean a config
            // entry per provider for a distinction nobody would act on.
            var completion = await llmProvider.CompleteAsync(
                new LlmCompletionRequest(
                    tenantId,
                    LlmTask.Classify,
                    [LlmMessage.System(SummaryInstructions), LlmMessage.User(transcript)],
                    Temperature: 0.2m,
                    MaxTokens: SummaryMaxTokens,
                    Tools: null),
                cancellationToken);

            // Staged, not saved: the ExecuteAndSaveInTenantScopeAsync below commits it
            // together with the handoff it paid for.
            runRecorder.Record(
                tenantId,
                agentId,
                completion.Usage,
                conversationId,
                wasHandoff: true);

            return string.IsNullOrWhiteSpace(completion.Content) ? null : completion.Content.Trim();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not summarize conversation {ConversationId} for its handoff; handing it over without one.",
                conversationId);

            return null;
        }
    }

    /// <summary>
    /// The exchange as plain labelled turns. Oldest first, because a summary of a
    /// conversation read backwards is a summary of a different conversation.
    /// </summary>
    private static string Transcript(IReadOnlyList<Message> history)
    {
        var builder = new StringBuilder();

        foreach (var message in history.Where(m => !string.IsNullOrWhiteSpace(m.Body)))
        {
            builder
                .Append(message.Direction == MessageDirection.Inbound ? "Cliente: " : "Asistente: ")
                .AppendLine(message.Body);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Opaque to callers, like every cursor in this API — but ticks rather than the
    /// milliseconds ORB-C02's document list uses, and the difference is a bug this list
    /// would otherwise have.
    ///
    /// That list pages <em>descending</em> with <c>&lt;</c>, so flooring the timestamp to
    /// a millisecond excludes the boundary row, which is what you want. This one pages
    /// <em>ascending</em> with <c>&gt;</c>, where flooring includes it again: the row's
    /// real microseconds are greater than its own floored cursor, so the last row of a
    /// page reappeared as the first row of the next one, forever, on a queue of two.
    /// Postgres stores microseconds, so ticks round-trip exactly.
    /// </summary>
    private static string EncodeCursor(DateTimeOffset requestedAt)
        => requestedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset? DecodeCursor(string? cursor)
        => long.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            && ticks is >= 0 and <= 3_155_378_975_999_999_999
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    private sealed record HandoffContext(Guid ContactId, string Transcript);
}
