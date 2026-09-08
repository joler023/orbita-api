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
}
