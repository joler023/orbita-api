using Moq;
using Orbita.Application.Outbox;
using Orbita.Domain.Outbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class OutboxWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StageAsync_AddsASerializedEventWithoutSaving()
    {
        var repository = new Mock<IOutboxEventRepository>();
        OutboxEvent? staged = null;
        repository
            .Setup(r => r.AddAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEvent, CancellationToken>((e, _) => staged = e)
            .Returns(Task.CompletedTask);
        var sut = new OutboxWriter(repository.Object, new FixedTimeProvider(Now));
        var tenantId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();

        await sut.StageAsync(tenantId, "Conversation", aggregateId, "conversation.opened", new { conversationId = aggregateId }, CancellationToken.None);

        Assert.NotNull(staged);
        Assert.Equal(tenantId, staged!.TenantId);
        Assert.Equal("Conversation", staged.AggregateType);
        Assert.Equal(aggregateId, staged.AggregateId);
        Assert.Equal("conversation.opened", staged.EventType);
        Assert.Contains(aggregateId.ToString(), staged.PayloadJson);
        Assert.Equal(Now, staged.OccurredAt);
        Assert.Null(staged.PublishedAt);
    }
}
