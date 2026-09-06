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
        DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
        FullName = fullName;
        EmailVerifiedAt = emailVerifiedAt;
        LastLoginAt = lastLoginAt;
        CreatedAt = createdAt;
    }

    public string Email { get; }

    public string PasswordHash { get; private set; }

    public string FullName { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <param name="passwordHash">
    /// An already-hashed password (see Application's IPasswordHasher). The domain never
    /// hashes credentials itself — that is an infrastructure concern.
    /// </param>
    public static User Create(string email, string passwordHash, string fullName, DateTimeOffset now)
    {
        var normalizedEmail = RequireNonEmpty(email, nameof(email)).ToLowerInvariant();
        if (!normalizedEmail.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Email must be a valid address.", nameof(email));
        }

        return new User(
            Guid.NewGuid(),
            normalizedEmail,
            RequireNonEmpty(passwordHash, nameof(passwordHash)),
            RequireLength(fullName, nameof(fullName), 1, FullNameMaxLength),
            emailVerifiedAt: null,
            lastLoginAt: null,
            createdAt: now);
    }

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    public void VerifyEmail(DateTimeOffset now) => EmailVerifiedAt = now;

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
