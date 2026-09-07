namespace Orbita.Application.Identity;

/// <summary>Two-factor authentication setup and verification (ORB-A11).</summary>
public interface ITwoFactorService
{
    /// <summary>
    /// Generates a new secret and stores it (encrypted) on the user, not yet enabled.
    /// Calling this again before confirming simply replaces the pending secret.
    /// </summary>
    Task<TwoFactorSetupResult> BeginSetupAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies <paramref name="code"/> against the pending secret and, if it matches,
    /// enables two-factor and issues a fresh batch of backup codes (returned once,
    /// raw — only their hashes are persisted).
    /// </summary>
    /// <exception cref="InvalidTwoFactorCodeException">The code doesn't match.</exception>
    Task<IReadOnlyList<string>> ConfirmSetupAsync(Guid userId, string code, CancellationToken cancellationToken);

    /// <exception cref="InvalidCredentialsException">The password is wrong.</exception>
    Task DisableAsync(Guid userId, string currentPassword, CancellationToken cancellationToken);

    /// <exception cref="InvalidOperationException">Two-factor isn't enabled for this user.</exception>
    Task<IReadOnlyList<string>> RegenerateBackupCodesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Tries <paramref name="code"/> as a TOTP code first, then as a backup code
    /// (marking it used on success). Used by <c>AuthenticationService</c> at login.
    /// </summary>
    Task<bool> VerifyLoginCodeAsync(Guid userId, string code, CancellationToken cancellationToken);
}
