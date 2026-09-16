using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Inbox;
using Orbita.Application.Outbox;
using Orbita.Domain.Common;

namespace Orbita.UnitTests.Application;

/// <summary>
/// The seam that moved ORB-C07's summary off the customer's path. What matters is that it
/// reacts only to its own event, sets the tenant before touching RLS'd tables, and never
/// turns a malformed row into an eternal retry.
/// </summary>
public sealed class HandoffSummaryIntegrationHandlerTests
{
    private readonly Mock<IConversationHandoffService> _handoffs = new();
    private readonly Mock<ITenantContextSetter> _tenantContext = new();
    private readonly HandoffSummaryIntegrationHandler _sut;

    public HandoffSummaryIntegrationHandlerTests()
        => _sut = new HandoffSummaryIntegrationHandler(
            _handoffs.Object, _tenantContext.Object, NullLogger<HandoffSummaryIntegrationHandler>.Instance);

    private static OutboxEnvelope Envelope(Guid tenantId, string payloadJson)
        => new(1, tenantId, "Conversation", Guid.NewGuid(), ConversationHandoffService.HandoffRequestedEventType, payloadJson, null, DateTimeOffset.UtcNow);

    [Fact]
    public void It_reacts_only_to_handoffs()
    {
        Assert.True(_sut.CanHandle("conversation.handoff_requested"));
        Assert.False(_sut.CanHandle("message.received"));
    }

    [Fact]
    public async Task It_writes_the_summary_inside_the_events_tenant()
    {
        var tenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _sut.HandleAsync(
            Envelope(tenantId, $$"""{"conversationId":"{{conversationId}}","contactId":"{{Guid.NewGuid()}}","agentId":"{{agentId}}","reason":"CustomerAsked"}"""),
            CancellationToken.None);

        _tenantContext.Verify(t => t.SetTenant(tenantId), Times.Once);
        _handoffs.Verify(h => h.WriteSummaryAsync(tenantId, conversationId, agentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_row_without_the_ids_is_skipped_rather_than_retried_forever()
    {
        await _sut.HandleAsync(Envelope(Guid.NewGuid(), """{"reason":"CustomerAsked"}"""), CancellationToken.None);

        _handoffs.VerifyNoOtherCalls();
    }
}
