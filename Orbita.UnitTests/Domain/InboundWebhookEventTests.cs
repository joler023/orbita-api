using Orbita.Domain.Channels;

namespace Orbita.UnitTests.Domain;

public sealed class InboundWebhookEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ValidHash = new('a', InboundWebhookEvent.PayloadHashLength);

    [Fact]
    public void Receive_SetsPendingStatusWithNoAttempts()
    {
        var evt = InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, "{}", ValidHash, Now);

        Assert.Equal(InboundWebhookStatus.Pending, evt.Status);
        Assert.Equal(0, evt.Attempts);
        Assert.Equal(Now, evt.ReceivedAt);
        Assert.Null(evt.ProcessedAt);
        Assert.Null(evt.LockedAt);
    }

    [Fact]
    public void Receive_WithNoPayload_Throws()
        => Assert.Throws<ArgumentException>(
            () => InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, string.Empty, ValidHash, Now));

    [Fact]
    public void Receive_WithAnEmptyTenantId_Throws()
        => Assert.Throws<ArgumentException>(
            () => InboundWebhookEvent.Receive(Guid.Empty, Guid.NewGuid(), ChannelKind.WhatsApp, "{}", ValidHash, Now));

    [Fact]
    public void Receive_WithAnEmptyChannelAccountId_Throws()
        => Assert.Throws<ArgumentException>(
            () => InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.Empty, ChannelKind.WhatsApp, "{}", ValidHash, Now));

    [Fact]
    public void Receive_WithAWrongLengthHash_Throws()
        => Assert.Throws<ArgumentException>(
            () => InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, "{}", "too-short", Now));

    [Fact]
    public void MarkProcessing_SetsProcessingAndLocksIt()
    {
        var evt = InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, "{}", ValidHash, Now);

        evt.MarkProcessing(Now);

        Assert.Equal(InboundWebhookStatus.Processing, evt.Status);
        Assert.Equal(Now, evt.LockedAt);
    }

    [Fact]
    public void MarkProcessed_SetsProcessedAndClearsTheLock()
    {
        var evt = InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, "{}", ValidHash, Now);
        evt.MarkProcessing(Now);

        evt.MarkProcessed(Now.AddSeconds(1));

        Assert.Equal(InboundWebhookStatus.Processed, evt.Status);
        Assert.Equal(Now.AddSeconds(1), evt.ProcessedAt);
        Assert.Null(evt.LockedAt);
    }

    [Fact]
    public void MarkFailed_BelowMaxAttempts_GoesBackToPendingForTheNextSweep()
    {
        var evt = InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, "{}", ValidHash, Now);
        evt.MarkProcessing(Now);

        evt.MarkFailed("boom", Now, maxAttempts: 3);

        Assert.Equal(InboundWebhookStatus.Pending, evt.Status);
        Assert.Equal(1, evt.Attempts);
        Assert.Equal("boom", evt.LastError);
        Assert.Null(evt.LockedAt);
    }

    [Fact]
    public void MarkFailed_ReachingMaxAttempts_BecomesDead()
    {
        var evt = InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, "{}", ValidHash, Now);

        evt.MarkFailed("e1", Now, maxAttempts: 2);
        evt.MarkFailed("e2", Now, maxAttempts: 2);

        Assert.Equal(InboundWebhookStatus.Dead, evt.Status);
        Assert.Equal(2, evt.Attempts);
    }

    [Fact]
    public void Requeue_ResetsADeadEventBackToPending()
    {
        var evt = InboundWebhookEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), ChannelKind.WhatsApp, "{}", ValidHash, Now);
        evt.MarkFailed("boom", Now, maxAttempts: 1);

        evt.Requeue();

        Assert.Equal(InboundWebhookStatus.Pending, evt.Status);
        Assert.Null(evt.LockedAt);
    }

    [Fact]
    public void Hash_IsALowercase64CharacterHexSha256()
    {
        var hash = InboundWebhookEvent.Hash("hello"u8);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Equal(hash, InboundWebhookEvent.Hash("hello"u8));
        Assert.NotEqual(hash, InboundWebhookEvent.Hash("hello!"u8));
    }
}
