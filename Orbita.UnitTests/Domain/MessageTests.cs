using Orbita.Domain.Inbox;

namespace Orbita.UnitTests.Domain;

public sealed class MessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Inbound_StartsDeliveredWithTheGivenFields()
    {
        var message = Message.Inbound(Guid.NewGuid(), Guid.NewGuid(), "wamid.abc", "hola", null, null, Now);

        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(MessageStatus.Delivered, message.Status);
        Assert.Equal(MessageCategory.Service, message.Category);
        Assert.Equal("wamid.abc", message.ExternalId);
        Assert.Equal("hola", message.Body);
        Assert.Equal(Now, message.DeliveredAt);
        Assert.Equal(Now, message.CreatedAt);
        Assert.Null(message.SentAt);
    }

    [Fact]
    public void Inbound_WithoutExternalId_Throws()
        => Assert.Throws<ArgumentException>(() => Message.Inbound(Guid.NewGuid(), Guid.NewGuid(), "", "hola", null, null, Now));

    [Fact]
    public void Inbound_WithAReplyContext_KeepsTheReferencedExternalId()
    {
        var message = Message.Inbound(Guid.NewGuid(), Guid.NewGuid(), "wamid.new", "sí", null, "wamid.previous", Now);

        Assert.Equal("wamid.previous", message.ReplyToExternalId);
    }

    [Fact]
    public void MarkMediaStored_SetsKeyAndMime()
    {
        var message = Message.Inbound(Guid.NewGuid(), Guid.NewGuid(), "wamid.abc", null, "image/jpeg", null, Now);

        message.MarkMediaStored("tenants/t/conversations/c/m.jpg", "image/jpeg");

        Assert.Equal("tenants/t/conversations/c/m.jpg", message.MediaKey);
        Assert.Equal("image/jpeg", message.MediaMime);
    }

    [Fact]
    public void MarkMediaStored_WithoutAKey_Throws()
    {
        var message = Message.Inbound(Guid.NewGuid(), Guid.NewGuid(), "wamid.abc", null, "image/jpeg", null, Now);

        Assert.Throws<ArgumentException>(() => message.MarkMediaStored("", "image/jpeg"));
    }

    private static Message QueuedOutbound()
        => Message.OutboundText(Guid.NewGuid(), Guid.NewGuid(), "hola", Guid.NewGuid(), MessageCategory.Service, Now);

    [Fact]
    public void MarkDelivered_SetsStatusAndDeliveredAt()
    {
        var message = QueuedOutbound();
        message.MarkSent("wamid.out", Now);

        message.MarkDelivered(Now.AddSeconds(5));

        Assert.Equal(MessageStatus.Delivered, message.Status);
        Assert.Equal(Now.AddSeconds(5), message.DeliveredAt);
    }

    [Fact]
    public void MarkDelivered_AfterRead_DoesNotDowngrade()
    {
        var message = QueuedOutbound();
        message.MarkSent("wamid.out", Now);
        message.MarkRead(Now.AddSeconds(5));

        message.MarkDelivered(Now.AddSeconds(10));

        Assert.Equal(MessageStatus.Read, message.Status);
        Assert.Equal(Now.AddSeconds(5), message.DeliveredAt);
    }

    [Fact]
    public void MarkRead_WithoutPriorDelivered_BackfillsDeliveredAt()
    {
        var message = QueuedOutbound();
        message.MarkSent("wamid.out", Now);

        message.MarkRead(Now.AddSeconds(5));

        Assert.Equal(MessageStatus.Read, message.Status);
        Assert.Equal(Now.AddSeconds(5), message.DeliveredAt);
        Assert.Equal(Now.AddSeconds(5), message.ReadAt);
    }

    [Fact]
    public void ResetForRetry_WhenFailed_RequeuesAndClearsError()
    {
        var message = QueuedOutbound();
        message.MarkFailed("130429");

        message.ResetForRetry();

        Assert.Equal(MessageStatus.Queued, message.Status);
        Assert.Null(message.ErrorCode);
    }

    [Fact]
    public void ResetForRetry_WhenNotFailed_Throws()
    {
        var message = QueuedOutbound();

        Assert.Throws<InvalidOperationException>(message.ResetForRetry);
    }
}
