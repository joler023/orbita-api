using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orbita.Application.Outbox;
using Orbita.Domain.Common;

namespace Orbita.Application.Ai;

/// <summary>
/// Wires ORB-C04 onto ORB-B04's outbox instead of onto <c>InboundMessageProcessor</c>.
///
/// That is the rule CLAUDE.md states for this repo — "the outbox is the integration seam
/// for anything reacting to a domain event, don't bolt side effects directly onto command
/// handlers" — and here it buys something concrete: answering a customer is slow (a model
/// call, six seconds at p95) while ingesting a webhook must not be, and Meta retries
/// anything it does not get a fast 200 for. Reacting to the committed event keeps the two
/// on separate clocks, and means a model outage delays replies instead of losing messages.
///
/// This is the first <see cref="IIntegrationEventHandler"/> in the codebase; the seam had
/// been empty since B04 landed.
/// </summary>
public sealed class AgentReplyIntegrationHandler(
    IAgentConversationResponder responder,
    ITenantContextSetter tenantContext,
    ILogger<AgentReplyIntegrationHandler> logger) : IIntegrationEventHandler
{
    public const string HandledEventType = "message.received";

    public bool CanHandle(string eventType)
        => string.Equals(eventType, HandledEventType, StringComparison.Ordinal);

    public async Task HandleAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!TryReadIds(envelope, out var conversationId, out var messageId))
        {
            logger.LogWarning(
                "Outbox event {OutboxId} is a {EventType} without the ids this handler needs.",
                envelope.Id, envelope.EventType);

            return;
        }

        // The publisher hands every handler one shared scope and sets no tenant — the
        // envelope is the only thing that knows which one this is. Everything below reads
        // and writes RLS'd tables, so without this they would all see zero rows.
        tenantContext.SetTenant(envelope.TenantId);

        await responder.RespondAsync(envelope.TenantId, conversationId, messageId, cancellationToken);
    }

    /// <summary>
    /// The payload is our own (staged by <c>InboundMessageProcessor</c>), but it is read
    /// back out of a database row that may have been written by an older version of this
    /// code, so a missing field is a warning and a skip rather than an exception — the
    /// dispatcher would otherwise count it as a failure and retry it forever.
    /// </summary>
    private static bool TryReadIds(OutboxEnvelope envelope, out Guid conversationId, out Guid messageId)
    {
        conversationId = Guid.Empty;
        messageId = Guid.Empty;

        try
        {
            using var payload = JsonDocument.Parse(envelope.PayloadJson);

            return payload.RootElement.TryGetProperty("conversationId", out var conversation)
                && conversation.TryGetGuid(out conversationId)
                && payload.RootElement.TryGetProperty("messageId", out var message)
                && message.TryGetGuid(out messageId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
