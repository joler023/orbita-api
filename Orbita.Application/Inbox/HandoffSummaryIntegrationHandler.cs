using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orbita.Application.Outbox;
using Orbita.Domain.Common;

namespace Orbita.Application.Inbox;

/// <summary>
/// Writes a handoff's summary in reaction to the committed handoff (ORB-C07), instead of
/// inside the request that made it.
///
/// Domain rule 3 in CLAUDE.md, and here it is not a matter of style: measured against the
/// managed database with a real model, writing the note inline made a customer who asked
/// for a person wait 13.8 s for the sentence telling them so, twice as long as an ordinary
/// answer, for a note that is not even for them. As a reaction to the event, the handoff
/// commits and the customer hears back straight away, and a provider outage becomes a
/// retry with backoff rather than a summary lost for good.
/// </summary>
public sealed class HandoffSummaryIntegrationHandler(
    IConversationHandoffService handoffs,
    ITenantContextSetter tenantContext,
    ILogger<HandoffSummaryIntegrationHandler> logger) : IIntegrationEventHandler
{
    public bool CanHandle(string eventType)
        => string.Equals(eventType, ConversationHandoffService.HandoffRequestedEventType, StringComparison.Ordinal);

    public async Task HandleAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!TryReadIds(envelope, out var conversationId, out var agentId))
        {
            // A warning and a skip, not an exception: the dispatcher would count a missing
            // field as a failure and retry a row that can never succeed, forever.
            logger.LogWarning(
                "Outbox event {OutboxId} is a {EventType} without the ids this handler needs.",
                envelope.Id, envelope.EventType);

            return;
        }

        // The publisher sets no tenant, and everything below reads RLS'd tables.
        tenantContext.SetTenant(envelope.TenantId);

        await handoffs.WriteSummaryAsync(envelope.TenantId, conversationId, agentId, cancellationToken);
    }

    private static bool TryReadIds(OutboxEnvelope envelope, out Guid conversationId, out Guid agentId)
    {
        conversationId = Guid.Empty;
        agentId = Guid.Empty;

        try
        {
            using var payload = JsonDocument.Parse(envelope.PayloadJson);

            return payload.RootElement.TryGetProperty("conversationId", out var conversation)
                && conversation.TryGetGuid(out conversationId)
                && payload.RootElement.TryGetProperty("agentId", out var agent)
                && agent.TryGetGuid(out agentId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
