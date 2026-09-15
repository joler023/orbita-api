using Orbita.Infrastructure.Channels;

namespace Orbita.IntegrationTests;

/// <summary>No database involved — lives here rather than Orbita.UnitTests only because Orbita.UnitTests doesn't reference Orbita.Infrastructure.</summary>
public sealed class TokenBucketRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_WithinBurstLimit_CompletesImmediately()
    {
        using var sut = new TokenBucketRateLimiter();
        var accountId = Guid.NewGuid();

        var task = sut.AcquireAsync(accountId, CancellationToken.None);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(500)));

        Assert.Same(task, completed);
    }

    [Fact]
    public async Task AcquireAsync_TracksEachChannelAccountSeparately()
    {
        using var sut = new TokenBucketRateLimiter();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        await sut.AcquireAsync(accountA, CancellationToken.None);
        var taskB = sut.AcquireAsync(accountB, CancellationToken.None);
        var completed = await Task.WhenAny(taskB, Task.Delay(TimeSpan.FromMilliseconds(500)));

        Assert.Same(taskB, completed);
    }
}
