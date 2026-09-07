using Moq;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class TwoFactorServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITwoFactorBackupCodeRepository> _backupCodes = new();
    private readonly Mock<ITotpProvider> _totp = new();
    private readonly Mock<IUserSecretProtector> _protector = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly TwoFactorService _sut;

    public TwoFactorServiceTests()
    {
        _protector.Setup(p => p.Protect(It.IsAny<string>())).Returns((string s) => $"encrypted({s})");
        _protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns((string s) => s.Replace("encrypted(", string.Empty).TrimEnd(')'));

        _sut = new TwoFactorService(
            _users.Object,
            _backupCodes.Object,
            _totp.Object,
            _protector.Object,
            _passwordHasher.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    private static User NewUser() => User.Create("jane@acme.com", "stored-hash", "Jane Doe", Now);

    [Fact]
    public async Task BeginSetupAsync_GeneratesAndStoresAnEncryptedSecret()
    {
        var user = NewUser();
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _totp.Setup(t => t.GenerateSecret()).Returns("RAWSECRET");
        _totp.Setup(t => t.BuildProvisioningUri("RAWSECRET", "jane@acme.com", "Orbita")).Returns("otpauth://totp/uri");

        var result = await _sut.BeginSetupAsync(user.Id, CancellationToken.None);

        Assert.Equal("RAWSECRET", result.Secret);
        Assert.Equal("otpauth://totp/uri", result.ProvisioningUri);
        Assert.Equal("encrypted(RAWSECRET)", user.TwoFactorSecretCiphertext);
        Assert.False(user.HasTwoFactorEnabled);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmSetupAsync_WithAValidCode_EnablesTwoFactorAndReturnsBackupCodes()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("encrypted(RAWSECRET)");
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _totp.Setup(t => t.ValidateCode("RAWSECRET", "123456")).Returns(true);

        var backupCodes = await _sut.ConfirmSetupAsync(user.Id, "123456", CancellationToken.None);

        Assert.True(user.HasTwoFactorEnabled);
        Assert.Equal(TwoFactorService.BackupCodeCount, backupCodes.Count);
        Assert.Equal(backupCodes.Count, backupCodes.Distinct().Count());
        _backupCodes.Verify(
            r => r.AddRangeAsync(It.Is<IReadOnlyCollection<TwoFactorBackupCode>>(c => c.Count == TwoFactorService.BackupCodeCount), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmSetupAsync_WithAnInvalidCode_ThrowsAndDoesNotEnable()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("encrypted(RAWSECRET)");
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _totp.Setup(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<InvalidTwoFactorCodeException>(() => _sut.ConfirmSetupAsync(user.Id, "000000", CancellationToken.None));

        Assert.False(user.HasTwoFactorEnabled);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmSetupAsync_WithoutAPendingSecret_Throws()
    {
        var user = NewUser();
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ConfirmSetupAsync(user.Id, "123456", CancellationToken.None));
    }

    [Fact]
    public async Task DisableAsync_WithTheCorrectPassword_ClearsTwoFactorAndInvalidatesBackupCodes()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("encrypted(RAWSECRET)");
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("stored-hash", "correct-horse-battery")).Returns(true);

        await _sut.DisableAsync(user.Id, "correct-horse-battery", CancellationToken.None);

        Assert.False(user.HasTwoFactorEnabled);
        _backupCodes.Verify(r => r.InvalidateAllForUserAsync(user.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisableAsync_WithTheWrongPassword_ThrowsInvalidCredentials()
    {
        var user = NewUser();
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => _sut.DisableAsync(user.Id, "wrong", CancellationToken.None));

        Assert.True(user.HasTwoFactorEnabled);
    }

    [Fact]
    public async Task RegenerateBackupCodesAsync_WhenNotEnabled_Throws()
    {
        var user = NewUser();
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RegenerateBackupCodesAsync(user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyLoginCodeAsync_WithAValidTotpCode_ReturnsTrue()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("encrypted(RAWSECRET)");
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _totp.Setup(t => t.ValidateCode("RAWSECRET", "123456")).Returns(true);

        Assert.True(await _sut.VerifyLoginCodeAsync(user.Id, "123456", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyLoginCodeAsync_WithAValidUnusedBackupCode_MarksItUsedAndReturnsTrue()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("encrypted(RAWSECRET)");
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _totp.Setup(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var backupCode = TwoFactorBackupCode.Issue(user.Id, "hash-of-abc123", Now);
        _backupCodes.Setup(r => r.GetByHashAsync(user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(backupCode);

        var verified = await _sut.VerifyLoginCodeAsync(user.Id, "abc123", CancellationToken.None);

        Assert.True(verified);
        Assert.True(backupCode.IsUsed);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyLoginCodeAsync_WithAnAlreadyUsedBackupCode_ReturnsFalse()
    {
        var user = NewUser();
        user.BeginTwoFactorSetup("encrypted(RAWSECRET)");
        user.EnableTwoFactor(Now);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _totp.Setup(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var backupCode = TwoFactorBackupCode.Issue(user.Id, "hash-of-abc123", Now);
        backupCode.MarkUsed(Now);
        _backupCodes.Setup(r => r.GetByHashAsync(user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(backupCode);

        Assert.False(await _sut.VerifyLoginCodeAsync(user.Id, "abc123", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyLoginCodeAsync_WhenTwoFactorIsNotEnabled_ReturnsFalse()
    {
        var user = NewUser();
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        Assert.False(await _sut.VerifyLoginCodeAsync(user.Id, "123456", CancellationToken.None));
    }
}
