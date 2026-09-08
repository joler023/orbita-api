using Moq;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class ChannelTokenExpiryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IChannelAccountRepository> _accounts = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ChannelTokenExpiryService _sut;

    public ChannelTokenExpiryServiceTests()
    {
        _sut = new ChannelTokenExpiryService(_accounts.Object, _unitOfWork.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task MarkExpiredAsync_FlipsEveryExpiringAccountAndSavesOnce()
    {
        var first = CreateAccount(Now.AddMinutes(-1));
        var second = CreateAccount(Now.AddDays(-2));
        _accounts.Setup(r => r.ListExpiringBeforeAsync(Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChannelAccount>)[first, second]);

        var count = await _sut.MarkExpiredAsync(CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(ChannelStatus.TokenExpired, first.Status);
        Assert.Equal(ChannelStatus.TokenExpired, second.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkExpiredAsync_WithNothingExpiring_DoesNotSave()
    {
        _accounts.Setup(r => r.ListExpiringBeforeAsync(Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChannelAccount>)[]);

        var count = await _sut.MarkExpiredAsync(CancellationToken.None);

        Assert.Equal(0, count);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ChannelAccount CreateAccount(DateTimeOffset tokenExpiresAt)
    {
        var account = ChannelAccount.ConnectWhatsApp(Guid.NewGuid(), Guid.NewGuid().ToString("N"), "waba", "Acme", null, "local://a", tokenExpiresAt, Now);
        account.MarkConnected(Now);
        return account;
    }
}
