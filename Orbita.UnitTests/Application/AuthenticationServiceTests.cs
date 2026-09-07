using Moq;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class AuthenticationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IssuedAccessToken StubAccessToken = new("jwt-value", Now.AddMinutes(15));

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IAccessTokenIssuer> _accessTokenIssuer = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ITwoFactorService> _twoFactor = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AuthenticationService _sut;

    public AuthenticationServiceTests()
    {
        _accessTokenIssuer.Setup(i => i.Issue(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>())).Returns(StubAccessToken);

        _sut = new AuthenticationService(
            _users.Object,
            _refreshTokens.Object,
            _accessTokenIssuer.Object,
            _passwordHasher.Object,
            _twoFactor.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    private static User NewUser() => User.Create("jane@acme.com", "stored-hash", "Jane Doe", Now);

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ResetsFailuresAndIssuesTokens()
    {
        var user = NewUser();
        _users.Setup(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("stored-hash", "correct-horse-battery")).Returns(true);

        var result = await _sut.LoginAsync(new LoginRequest("jane@acme.com", "correct-horse-battery"), CancellationToken.None);

        Assert.Equal("jwt-value", result.AccessToken);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.NotNull(user.LastLoginAt);
        _refreshTokens.Verify(r => r.AddAsync(It.Is<RefreshToken>(t => t.UserId == user.Id), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_NormalizesEmailBeforeLookup()
    {
        _users.Setup(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.LoginAsync(new LoginRequest("Jane@ACME.com", "whatever"), CancellationToken.None));

        _users.Verify(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ThrowsInvalidCredentials()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.LoginAsync(new LoginRequest("jane@acme.com", "whatever"), CancellationToken.None));

        _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_RegistersFailureAndThrows()
    {
        var user = NewUser();
        _users.Setup(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.LoginAsync(new LoginRequest("jane@acme.com", "wrong-password"), CancellationToken.None));

        Assert.Equal(1, user.FailedLoginAttempts);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenLockedOut_ThrowsWithoutCheckingPassword()
    {
        var user = NewUser();
        for (var i = 0; i < AuthenticationService.MaxFailedLoginAttempts; i++)
        {
            user.RegisterFailedLogin(Now, AuthenticationService.MaxFailedLoginAttempts, AuthenticationService.LockoutDuration);
        }

        _users.Setup(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.LoginAsync(new LoginRequest("jane@acme.com", "correct-horse-battery"), CancellationToken.None));

        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithTwoFactorEnabledAndNoCode_ThrowsTwoFactorRequired()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("ciphertext");
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("stored-hash", "correct-horse-battery")).Returns(true);

        await Assert.ThrowsAsync<TwoFactorRequiredException>(
            () => _sut.LoginAsync(new LoginRequest("jane@acme.com", "correct-horse-battery"), CancellationToken.None));

        _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithTwoFactorEnabledAndAValidCode_IssuesTokens()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("ciphertext");
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("stored-hash", "correct-horse-battery")).Returns(true);
        _twoFactor.Setup(t => t.VerifyLoginCodeAsync(user.Id, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.LoginAsync(new LoginRequest("jane@acme.com", "correct-horse-battery", "123456"), CancellationToken.None);

        Assert.Equal(user.Id, result.UserId);
        _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithTwoFactorEnabledAndAWrongCode_RegistersFailureAndThrowsInvalidCredentials()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("ciphertext");
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("stored-hash", "correct-horse-battery")).Returns(true);
        _twoFactor.Setup(t => t.VerifyLoginCodeAsync(user.Id, "000000", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.LoginAsync(new LoginRequest("jane@acme.com", "correct-horse-battery", "000000"), CancellationToken.None));

        Assert.Equal(1, user.FailedLoginAttempts);
        _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_RevokesOldTokenAndIssuesNewOneInTheSameFamily()
    {
        var user = NewUser();
        var oldToken = RefreshToken.IssueNewFamily(user.Id, "hash-of-old", Now.AddMinutes(-1), TimeSpan.FromDays(30));
        _refreshTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(oldToken);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _sut.RefreshAsync("raw-old-token", CancellationToken.None);

        Assert.Equal(user.Id, result.UserId);
        Assert.NotNull(oldToken.RevokedAt);
        Assert.NotNull(oldToken.ReplacedByTokenId);
        _refreshTokens.Verify(
            r => r.AddAsync(It.Is<RefreshToken>(t => t.FamilyId == oldToken.FamilyId && t.Id == oldToken.ReplacedByTokenId), It.IsAny<CancellationToken>()),
            Times.Once);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WithUnknownToken_Throws()
    {
        _refreshTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((RefreshToken?)null);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => _sut.RefreshAsync("unknown", CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_WithAlreadyRevokedToken_RevokesWholeFamilyAndThrows()
    {
        var reused = RefreshToken.IssueNewFamily(Guid.NewGuid(), "hash", Now.AddDays(-1), TimeSpan.FromDays(30));
        reused.RevokeReplacedBy(Guid.NewGuid(), Now.AddMinutes(-30));
        _refreshTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(reused);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => _sut.RefreshAsync("stolen-token", CancellationToken.None));

        _refreshTokens.Verify(r => r.RevokeFamilyAsync(reused.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_WithExpiredToken_ThrowsWithoutRevokingTheFamily()
    {
        var expired = RefreshToken.IssueNewFamily(Guid.NewGuid(), "hash", Now.AddDays(-31), TimeSpan.FromDays(30));
        _refreshTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expired);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => _sut.RefreshAsync("expired-token", CancellationToken.None));

        _refreshTokens.Verify(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_WithActiveToken_RevokesIt()
    {
        var token = RefreshToken.IssueNewFamily(Guid.NewGuid(), "hash", Now, TimeSpan.FromDays(30));
        _refreshTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        await _sut.LogoutAsync("raw-token", CancellationToken.None);

        Assert.NotNull(token.RevokedAt);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WithUnknownToken_IsANoOp()
    {
        _refreshTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((RefreshToken?)null);

        await _sut.LogoutAsync("unknown", CancellationToken.None);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
