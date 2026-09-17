using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class ConversationRepository(OrbitaDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Conversations.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Conversation?> FindOpenByContactAndAccountAsync(Guid contactId, Guid channelAccountId, CancellationToken cancellationToken)
        => dbContext.Conversations.SingleOrDefaultAsync(
            c => c.ContactId == contactId && c.ChannelAccountId == channelAccountId,
            cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
        => await dbContext.Conversations.AddAsync(conversation, cancellationToken);

    /// <summary>
    /// Joined to <c>contacts</c> in the query rather than resolved afterwards: the caller
    /// needs a name per row, and doing it here is one round trip instead of one per
    /// conversation. Both tables carry the same tenant query filter, so the join cannot
    /// reach across tenants even before Row Level Security has its say.
    /// </summary>
    /// <summary>
    /// Raw SQL rather than LINQ because the page and the total have to come from one
    /// statement: see the note on <see cref="IConversationRepository"/>. The CTE is the
    /// whole queue, the total counts it, and the page is the cursor's slice of it — one
    /// snapshot, so the two can never contradict each other.
    /// </summary>
    public async Task<HandoffQueuePageResult> ListWaitingForHumanAsync(
        Guid tenantId,
        DateTimeOffset? waitingSince,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQuery<HandoffQueueRow>($"""
                WITH waiting AS (
                    SELECT  c.id, c.contact_id, c.handoff_reason, c.handoff_requested_at,
                            c.handoff_summary, c.last_message_at, c.last_message_preview
                    FROM    conversations c
                    WHERE   c.tenant_id = {tenantId}
                      AND   c.handoff_requested_at IS NOT NULL
                ),
                page AS (
                    SELECT  *
                    FROM    waiting
                    WHERE   {waitingSince}::timestamptz IS NULL
                       OR   handoff_requested_at > {waitingSince}::timestamptz
                    ORDER BY handoff_requested_at
                    LIMIT   {limit}
                )
                SELECT  page.id                      AS "ConversationId",
                        page.contact_id              AS "ContactId",
                        ct.display_name              AS "ContactName",
                        page.handoff_reason          AS "Reason",
                        page.handoff_requested_at    AS "RequestedAt",
                        page.handoff_summary         AS "Summary",
                        page.last_message_at         AS "LastMessageAt",
                        page.last_message_preview    AS "LastMessagePreview",
                        (SELECT count(*) FROM waiting) AS "Total"
                FROM    page
                JOIN    contacts ct ON ct.id = page.contact_id
                ORDER BY page.handoff_requested_at
                """)
            .ToListAsync(cancellationToken);

        // The total rides on every row, so an empty page needs its own count — and an
        // empty page means an empty queue, since the cursor only ever moves forward.
        var total = rows.Count > 0
            ? (int)rows[0].Total
            : await dbContext.Conversations.CountAsync(
                c => c.TenantId == tenantId && c.HandoffRequestedAt != null, cancellationToken);

        return new HandoffQueuePageResult(
            [.. rows.Select(row => new HandoffQueueEntry(
                row.ConversationId,
                row.ContactId,
                row.ContactName,
                Enum.Parse<HandoffReason>(row.Reason),
                row.RequestedAt,
                row.Summary,
                row.LastMessageAt,
                row.LastMessagePreview))],
            total);
    }

    /// <summary>Shape of the projection above; <c>SqlQuery</c> needs a type to map columns onto.</summary>
    private sealed record HandoffQueueRow(
        Guid ConversationId,
        Guid ContactId,
        string ContactName,
        string Reason,
        DateTimeOffset RequestedAt,
        string? Summary,
        DateTimeOffset? LastMessageAt,
        string? LastMessagePreview,
        long Total);
}
