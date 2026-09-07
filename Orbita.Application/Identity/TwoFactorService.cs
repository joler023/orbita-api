using System.Security.Cryptography;
using System.Text;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

public sealed class TwoFactorService(
    IUserRepository userRepository,
    ITwoFactorBackupCodeRepository backupCodeRepository,
    ITotpProvider totpProvider,
    IUserSecretProtector secretProtector,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ITwoFactorService
{
    public const int BackupCodeCount = 10;
    private const string Issuer = "Orbita";

    public async Task<TwoFactorSetupResult> BeginSetupAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);

        var secret = totpProvider.GenerateSecret();
        user.BeginTwoFactorSetup(secretProtector.Protect(secret));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var provisioningUri = totpProvider.BuildProvisioningUri(secret, user.Email, Issuer);
        return new TwoFactorSetupResult(secret, provisioningUri);
    }

    public async Task<IReadOnlyList<string>> ConfirmSetupAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        if (user.TwoFactorSecretCiphertext is null)
        {
            throw new InvalidOperationException("Two-factor setup has not been started for this user.");
        }

        var secret = secretProtector.Unprotect(user.TwoFactorSecretCiphertext);
        if (!totpProvider.ValidateCode(secret, code))
        {
            throw new InvalidTwoFactorCodeException();
        }

        var now = timeProvider.GetUtcNow();
        user.EnableTwoFactor(now);
        var rawCodes = await IssueBackupCodesAsync(user.Id, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return rawCodes;
    }

    public async Task DisableAsync(Guid userId, string currentPassword, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        if (!passwordHasher.Verify(user.PasswordHash, currentPassword))
        {
            throw new InvalidCredentialsException();
        }

        user.DisableTwoFactor();
        await backupCodeRepository.InvalidateAllForUserAsync(userId, timeProvider.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> RegenerateBackupCodesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        if (!user.HasTwoFactorEnabled)
        {
            throw new InvalidOperationException("Two-factor authentication is not enabled for this user.");
        }

        var now = timeProvider.GetUtcNow();
        await backupCodeRepository.InvalidateAllForUserAsync(userId, now, cancellationToken);
        var rawCodes = await IssueBackupCodesAsync(userId, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return rawCodes;
    }

    public async Task<bool> VerifyLoginCodeAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.TwoFactorSecretCiphertext is null)
        {
            return false;
        }

        if (totpProvider.ValidateCode(secretProtector.Unprotect(user.TwoFactorSecretCiphertext), code))
        {
            return true;
        }

        var backupCode = await backupCodeRepository.GetByHashAsync(userId, Hash(code), cancellationToken);
        if (backupCode is null || backupCode.IsUsed)
        {
            return false;
        }

        backupCode.MarkUsed(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<string>> IssueBackupCodesAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rawCodes = Enumerable.Range(0, BackupCodeCount).Select(_ => GenerateBackupCode()).ToList();
        var entities = rawCodes.Select(code => TwoFactorBackupCode.Issue(userId, Hash(code), now)).ToList();
        await backupCodeRepository.AddRangeAsync(entities, cancellationToken);
        return rawCodes;
    }

    private static string GenerateBackupCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(5));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken cancellationToken)
        => await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");
}
