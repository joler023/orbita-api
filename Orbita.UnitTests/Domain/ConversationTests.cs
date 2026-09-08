using Orbita.Domain.Inbox;

namespace Orbita.UnitTests.Domain;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_StartsOpenWithNoUnreadMessages()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.Equal(ConversationStatus.Open, conversation.Status);
        Assert.Equal(0, conversation.UnreadCount);
        Assert.Equal(Now, conversation.CreatedAt);
    }

    [Fact]
    public void RegisterInbound_ReopensClosedAndResetsWindow()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        conversation.Close(Now);

        conversation.RegisterInbound(Now.AddDays(1), "hola");

        Assert.Equal(ConversationStatus.Open, conversation.Status);
        Assert.Null(conversation.ClosedAt);
        Assert.Equal(Now.AddDays(1) + Conversation.ServiceWindow, conversation.WindowExpiresAt);
        Assert.Equal(1, conversation.UnreadCount);
        Assert.Equal("hola", conversation.LastMessagePreview);
    }

    [Fact]
    public void RegisterInbound_TwiceIncrementsUnreadCount()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        conversation.RegisterInbound(Now, "one");
        conversation.RegisterInbound(Now.AddMinutes(1), "two");

        Assert.Equal(2, conversation.UnreadCount);
        Assert.Equal("two", conversation.LastMessagePreview);
    }

    [Fact]
    public void RegisterInbound_TruncatesAnOverlyLongPreview()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var longBody = new string('a', 500);

        conversation.RegisterInbound(Now, longBody);

        Assert.Equal(Conversation.LastMessagePreviewMaxLength, conversation.LastMessagePreview!.Length);
    }

    [Fact]
    public void IsWindowOpen_ExactlyAt24h_IsFalse()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        conversation.RegisterInbound(Now, "hola");

        Assert.False(conversation.IsWindowOpen(Now + Conversation.ServiceWindow));
        Assert.True(conversation.IsWindowOpen(Now + Conversation.ServiceWindow - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CanSendFreeForm_MirrorsIsWindowOpen()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.False(conversation.CanSendFreeForm(Now));

        conversation.RegisterInbound(Now, "hola");

        Assert.True(conversation.CanSendFreeForm(Now));
    }

    [Fact]
    public void RegisterOutbound_ByHuman_SetsFirstResponseSecondsOnce()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        conversation.RegisterInbound(Now, "hola");

        conversation.RegisterOutbound(Now.AddSeconds(30), "hi", isHuman: true);
        conversation.RegisterOutbound(Now.AddMinutes(5), "again", isHuman: true);

        Assert.Equal(30, conversation.FirstResponseSeconds);
    }

    [Fact]
    public void RegisterOutbound_ByNonHuman_DoesNotSetFirstResponseSeconds()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        conversation.RegisterInbound(Now, "hola");

        conversation.RegisterOutbound(Now.AddSeconds(30), "bot reply", isHuman: false);

        Assert.Null(conversation.FirstResponseSeconds);
    }

    [Fact]
    public void MarkRead_ResetsUnreadCount()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        conversation.RegisterInbound(Now, "hola");

        conversation.MarkRead();

        Assert.Equal(0, conversation.UnreadCount);
    }

    [Fact]
    public void Close_SetsClosedStatusAndTimestamp()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        conversation.Close(Now.AddHours(2));

        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        Assert.Equal(Now.AddHours(2), conversation.ClosedAt);
    }
}
