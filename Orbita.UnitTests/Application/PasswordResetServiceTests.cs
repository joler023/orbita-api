using Moq;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class PasswordResetServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordResetTokenRepository> _resetTokens = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IPasswordResetEmailSender> _emailSender = new();
    private readonly PasswordResetService _sut;

    public PasswordResetServiceTests()
    {
        _sut = new PasswordResetService(
            _users.Object,
            _resetTokens.Object,
            _refreshTokens.Object,
            _unitOfWork.Object,
            _passwordHasher.Object,
            _emailSender.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task RequestAsync_ForAnExistingEmail_InvalidatesOldTokensIssuesANewOneAndSendsAnEmail()
    {
        var user = User.Create("person@acme.com", "hash", "Person", Now);
        _users.Setup(r => r.GetByEmailAsync("person@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await _sut.RequestAsync("person@acme.com", CancellationToken.None);

        _resetTokens.Verify(r => r.InvalidateForUserAsync(user.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
        _resetTokens.Verify(r => r.AddAsync(It.Is<PasswordResetToken>(t => t.UserId == user.Id), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(s => s.SendAsync("person@acme.com", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestAsync_ForAnUnknownEmail_DoesNothingButDoesNotThrow()
    {
        _users.Setup(r => r.GetByEmailAsync("nobody@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await _sut.RequestAsync("nobody@acme.com", CancellationToken.None);

        _resetTokens.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetAsync_WithAValidToken_SetsTheNewPasswordAndRevokesAllSessions()
    {
        var user = User.Create("person@acme.com", "old-hash", "Person", Now);
        var token = PasswordResetToken.Issue(user.Id, "hash", Now, TimeSpan.FromHours(1));
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Hash("new-password-123")).Returns("new-hash");

        await _sut.ResetAsync(new ResetPasswordRequest("raw-token", "new-password-123"), CancellationToken.None);

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.NotNull(token.UsedAt);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokens.Verify(r => r.RevokeAllForUserAsync(user.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetAsync_WithAnUnknownToken_ThrowsInvalidPasswordReset()
    {
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PasswordResetToken?)null);

        await Assert.ThrowsAsync<InvalidPasswordResetException>(
            () => _sut.ResetAsync(new ResetPasswordRequest("bad-token", "new-password-123"), CancellationToken.None));

        _refreshTokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetAsync_WithAnExpiredToken_ThrowsInvalidPasswordReset()
    {
        var expired = PasswordResetToken.Issue(Guid.NewGuid(), "hash", Now.AddHours(-2), TimeSpan.FromHours(1));
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expired);

        await Assert.ThrowsAsync<InvalidPasswordResetException>(
            () => _sut.ResetAsync(new ResetPasswordRequest("token", "new-password-123"), CancellationToken.None));
    }

    [Fact]
    public async Task ResetAsync_WithAnAlreadyUsedToken_ThrowsInvalidPasswordReset()
    {
        var token = PasswordResetToken.Issue(Guid.NewGuid(), "hash", Now, TimeSpan.FromHours(1));
        token.MarkUsed(Now);
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        await Assert.ThrowsAsync<InvalidPasswordResetException>(
            () => _sut.ResetAsync(new ResetPasswordRequest("token", "new-password-123"), CancellationToken.None));
    }
}
