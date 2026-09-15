using Orbita.Domain.Inbox;

namespace Orbita.UnitTests.Domain;

/// <summary>
/// The frontend asked for this rather than reading "sentByUserId is null" as "the
/// assistant wrote it", and the reason is worth pinning: that equivalence holds only
/// while the assistant is the single non-human sender in the product. These tests are
/// what will fail — loudly, instead of silently mislabelling a customer conversation —
/// the day a scheduled template or an automation sends something with no person behind it.
/// </summary>
public sealed class MessageAuthorKindTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();

    [Fact]
    public void A_customer_message_is_human()
    {
        var message = Message.Inbound(TenantId, ConversationId, "wamid.1", "hola", null, null, Now);

        Assert.Equal(MessageAuthorKind.Human, message.AuthorKind);
    }

    [Fact]
    public void A_message_an_agent_typed_is_human()
    {
        var message = Message.OutboundText(
            TenantId, ConversationId, "ya te ayudo", Guid.NewGuid(), MessageCategory.Service, Now);

        Assert.Equal(MessageAuthorKind.Human, message.AuthorKind);
    }

    [Fact]
    public void An_assistant_reply_is_an_ai_agent_and_carries_its_run()
    {
        var runId = Guid.NewGuid();
        var message = Message.OutboundAgentText(TenantId, ConversationId, "abrimos a las 7", runId, Now);

        Assert.Equal(MessageAuthorKind.AiAgent, message.AuthorKind);

        // The run id is what lets a screen show which passages the answer came from.
        Assert.Equal(runId, message.AiRunId);
    }

    [Fact]
    public void An_outbound_message_with_neither_is_the_system()
    {
        // Nothing produces this today. It is the case that makes `sentByUserId == null`
        // an unsafe test for "the assistant wrote it", which is the whole point.
        var message = Message.OutboundText(
            TenantId, ConversationId, "tu pedido va en camino", sentByUserId: null, MessageCategory.Utility, Now);

        Assert.Equal(MessageAuthorKind.System, message.AuthorKind);
    }
}
