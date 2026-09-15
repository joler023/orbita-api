using Moq;
using Orbita.Application.Outbox;
using Orbita.Domain.Common;
using Orbita.Domain.Outbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class OutboxDispatchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOutboxEventRepository> _repository = new();
    private readonly Mock<IIntegrationEventPublisher> _publisher = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly OutboxDispatchService _sut;

    public OutboxDispatchServiceTests()
    {
        _sut = new OutboxDispatchService(_repository.Object, _publisher.Object, _unitOfWork.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task DispatchOnce_PublishesAndMarks()
    {
        var evt = OutboxEvent.Stage(Guid.NewGuid(), "Message", Guid.NewGuid(), "message.received", "{}", Now.AddSeconds(-1));
        _repository.Setup(r => r.DequeuePendingAsync(10, Now, It.IsAny<CancellationToken>())).ReturnsAsync([evt]);

        await _sut.DispatchOnceAsync(10, CancellationToken.None);

        _publisher.Verify(p => p.PublishAsync(It.Is<OutboxEnvelope>(e => e.EventType == "message.received"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(Now, evt.PublishedAt);
        Assert.Equal(0, evt.Attempts);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchOnce_WhenPublisherThrows_IncrementsAttemptsAndKeepsPending()
    {
        var evt = OutboxEvent.Stage(Guid.NewGuid(), "Message", Guid.NewGuid(), "message.received", "{}", Now.AddSeconds(-1));
        _repository.Setup(r => r.DequeuePendingAsync(10, Now, It.IsAny<CancellationToken>())).ReturnsAsync([evt]);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<OutboxEnvelope>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        await _sut.DispatchOnceAsync(10, CancellationToken.None);

        Assert.Null(evt.PublishedAt);
        Assert.Equal(1, evt.Attempts);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchOnce_OneEventFailing_StillPublishesTheRest()
    {
        var failing = OutboxEvent.Stage(Guid.NewGuid(), "Message", Guid.NewGuid(), "message.received", "{}", Now.AddSeconds(-1));
        var succeeding = OutboxEvent.Stage(Guid.NewGuid(), "Conversation", Guid.NewGuid(), "conversation.opened", "{}", Now.AddSeconds(-1));
        _repository.Setup(r => r.DequeuePendingAsync(10, Now, It.IsAny<CancellationToken>())).ReturnsAsync([failing, succeeding]);
        _publisher
            .Setup(p => p.PublishAsync(It.Is<OutboxEnvelope>(e => e.EventType == "message.received"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await _sut.DispatchOnceAsync(10, CancellationToken.None);

        Assert.Null(failing.PublishedAt);
        Assert.Equal(Now, succeeding.PublishedAt);
    }

    [Fact]
    public async Task DispatchOnce_WithNothingPending_DoesNotSave()
    {
        _repository.Setup(r => r.DequeuePendingAsync(10, Now, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await _sut.DispatchOnceAsync(10, CancellationToken.None);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
