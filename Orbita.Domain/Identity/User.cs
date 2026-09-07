using Orbita.Domain.Common;

namespace Orbita.Domain.Identity;

/// <summary>
/// A person who can sign in. Global, not tenant-scoped — the same user can belong to
/// several organizations through <see cref="Membership"/> (orbita-schema.dbml: users).
/// </summary>
public sealed class User : Entity
{
    public const int FullNameMaxLength = 200;

    private User(
        Guid id,
        string email,
        string passwordHash,
        string fullName,
        DateTimeOffset? emailVerifiedAt,
        DateTimeOffset? lastLoginAt,
        DateTimeOffset? passwordSetAt,
        int failedLoginAttempts,
        DateTimeOffset? lockedUntil,
        string? twoFactorSecretCiphertext,
        DateTimeOffset? twoFactorEnabledAt,
        DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
        FullName = fullName;
        EmailVerifiedAt = emailVerifiedAt;
        LastLoginAt = lastLoginAt;
        PasswordSetAt = passwordSetAt;
        FailedLoginAttempts = failedLoginAttempts;
        LockedUntil = lockedUntil;
        TwoFactorSecretCiphertext = twoFactorSecretCiphertext;
        TwoFactorEnabledAt = twoFactorEnabledAt;
        CreatedAt = createdAt;
    }

    public string Email { get; }

    public string PasswordHash { get; private set; }

    public string FullName { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>
    /// Not in orbita-schema.dbml (added for ORB-A07). Null means PasswordHash is an
    /// unusable placeholder generated when this User was created by an invitation,
    /// not chosen by the person themselves — see <see cref="CreateInvited"/>. Login
    /// still works correctly without checking this flag anywhere, since a
    /// placeholder hash never verifies against anything a person could type; it
    /// exists so ORB-A07's accept flow knows whether to require a new password or
    /// verify the existing one.
    /// </summary>
    public DateTimeOffset? PasswordSetAt { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// The TOTP secret (RFC 6238), encrypted at rest via <c>IUserSecretProtector</c>
    /// (ORB-A11) — never the raw base32 secret. Set as soon as setup begins, before
    /// <see cref="TwoFactorEnabledAt"/> is set, so a user who starts setup and
    /// abandons it just overwrites this on their next attempt instead of needing a
    /// separate "pending secret" table.
    /// </summary>
    public string? TwoFactorSecretCiphertext { get; private set; }

    /// <summary>Null until the person confirms setup with a valid code (ORB-A11).</summary>
    public DateTimeOffset? TwoFactorEnabledAt { get; private set; }

    public bool HasTwoFactorEnabled => TwoFactorEnabledAt is not null;

    public DateTimeOffset CreatedAt { get; }

    /// <param name="passwordHash">
    /// An already-hashed password (see Application's IPasswordHasher). The domain never
    /// hashes credentials itself — that is an infrastructure concern.
    /// </param>
    public static User Create(string email, string passwordHash, string fullName, DateTimeOffset now)
        => new(
            Guid.NewGuid(),
            NormalizeEmail(email),
            RequireNonEmpty(passwordHash, nameof(passwordHash)),
            RequireLength(fullName, nameof(fullName), 1, FullNameMaxLength),
            emailVerifiedAt: null,
            lastLoginAt: null,
            passwordSetAt: now,
            failedLoginAttempts: 0,
            lockedUntil: null,
            twoFactorSecretCiphertext: null,
            twoFactorEnabledAt: null,
            createdAt: now);

    /// <summary>
    /// A person invited by someone else, before they have chosen their own
    /// credentials (ORB-A07). <paramref name="placeholderPasswordHash"/> must be an
    /// unguessable, unusable value (e.g. the hash of a random token) — never a real
    /// password — since nothing prevents a login attempt from reaching it.
    /// </summary>
    public static User CreateInvited(string email, string fullName, string placeholderPasswordHash, DateTimeOffset now)
        => new(
            Guid.NewGuid(),
            NormalizeEmail(email),
            RequireNonEmpty(placeholderPasswordHash, nameof(placeholderPasswordHash)),
            RequireLength(fullName, nameof(fullName), 1, FullNameMaxLength),
            emailVerifiedAt: null,
            lastLoginAt: null,
            passwordSetAt: null,
            failedLoginAttempts: 0,
            lockedUntil: null,
            twoFactorSecretCiphertext: null,
            twoFactorEnabledAt: null,
            createdAt: now);

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    public void VerifyEmail(DateTimeOffset now) => EmailVerifiedAt = now;

    public void Rename(string fullName) => FullName = RequireLength(fullName, nameof(fullName), 1, FullNameMaxLength);

    /// <param name="newPasswordHash">Already hashed — see <see cref="Create"/>.</param>
    public void SetPassword(string newPasswordHash, DateTimeOffset now)
    {
        PasswordHash = RequireNonEmpty(newPasswordHash, nameof(newPasswordHash));
        PasswordSetAt = now;
    }

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// Called after a failed password check. Locks the account once
    /// <paramref name="maxAttempts"/> is reached, for <paramref name="lockoutDuration"/>
    /// from now — the counter keeps accumulating past the threshold so a locked-out
    /// account does not quietly unlock itself early from more failed attempts.
    /// </summary>
    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
        {
            LockedUntil = now + lockoutDuration;
        }
    }

    public void ResetFailedLogins()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }

    /// <summary>
    /// Stores a freshly generated, encrypted TOTP secret, ready to be confirmed
    /// (ORB-A11). Does not enable two-factor by itself — <see cref="EnableTwoFactor"/>
    /// does, once the person proves they can generate a matching code.
    /// </summary>
    public void BeginTwoFactorSetup(string secretCiphertext) => TwoFactorSecretCiphertext = RequireNonEmpty(secretCiphertext, nameof(secretCiphertext));

    public void EnableTwoFactor(DateTimeOffset now) => TwoFactorEnabledAt = now;

    public void DisableTwoFactor()
    {
        TwoFactorSecretCiphertext = null;
        TwoFactorEnabledAt = null;
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = RequireNonEmpty(email, nameof(email)).ToLowerInvariant();
        if (!normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Email must be a valid address.", nameof(email));
        }

        return normalized;
    }

    private static string RequireNonEmpty(string value, string paramName)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"{paramName} must not be empty.", paramName);
        }

        return trimmed;
    }

    private static string RequireLength(string value, string paramName, int minLength, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"{paramName} must be between {minLength} and {maxLength} characters.",
                paramName);
        }

        return trimmed;
    }
}
