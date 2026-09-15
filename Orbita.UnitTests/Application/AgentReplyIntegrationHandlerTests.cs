using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Ai;
using Orbita.Application.Outbox;
using Orbita.Domain.Common;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C04's half of the outbox seam. Nothing here decides what to say — it decides
/// whether this event is one of ours, and puts the tenant in scope before anything reads
/// an RLS'd table. Getting that second part wrong does not throw: it silently finds no
/// rows, which is the failure this test exists to make loud.
/// </summary>
public sealed class AgentReplyIntegrationHandlerTests
{
    private readonly Mock<IAgentConversationResponder> _responder = new();
    private readonly Mock<ITenantContextSetter> _tenantContext = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly Guid _messageId = Guid.NewGuid();

    private readonly AgentReplyIntegrationHandler _sut;

    public AgentReplyIntegrationHandlerTests()
        => _sut = new AgentReplyIntegrationHandler(
            _responder.Object, _tenantContext.Object, NullLogger<AgentReplyIntegrationHandler>.Instance);

    private OutboxEnvelope Envelope(string payloadJson, string eventType = "message.received")
        => new(1, _tenantId, "Message", _messageId, eventType, payloadJson, null, DateTimeOffset.UtcNow);

    private string ValidPayload()
        => $$"""{"messageId":"{{_messageId}}","conversationId":"{{_conversationId}}","direction":"Inbound"}""";

    [Theory]
    [InlineData("message.received", true)]
    [InlineData("message.queued", false)]
    [InlineData("conversation.opened", false)]
    [InlineData("message.media_failed", false)]
    public void It_only_claims_the_event_it_reacts_to(string eventType, bool expected)
        => Assert.Equal(expected, _sut.CanHandle(eventType));

    [Fact]
    public async Task It_puts_the_tenant_in_scope_before_asking_for_a_reply()
    {
        var order = new List<string>();
        _tenantContext.Setup(t => t.SetTenant(_tenantId)).Callback(() => order.Add("tenant"));
        _responder
            .Setup(r => r.RespondAsync(_tenantId, _conversationId, _messageId, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("respond"))
            .ReturnsAsync(AgentReplyOutcome.Skipped(AgentReplyDecision.NoAgentAssigned));

        await _sut.HandleAsync(Envelope(ValidPayload()), CancellationToken.None);

        // The publisher hands every handler one shared scope and sets no tenant, so the
        // order is the whole contract: reading first would read nothing.
        Assert.Equal(["tenant", "respond"], order);
    }

    [Theory]
    [InlineData("""{"conversationId":"not-a-guid","messageId":"also-not"}""")]
    [InlineData("""{"messageId":"11111111-1111-4111-8111-111111111111"}""")]
    [InlineData("""{"direction":"Inbound"}""")]
    [InlineData("no es json")]
    public async Task A_payload_it_cannot_read_is_skipped_rather_than_thrown(string payloadJson)
    {
        await _sut.HandleAsync(Envelope(payloadJson), CancellationToken.None);

        // Throwing would make the dispatcher count this as a failed publish and retry it
        // with backoff forever — a malformed row never becomes readable.
        _responder.Verify(
            r => r.RespondAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
