using Orbita.Domain.Common;

namespace Orbita.Domain.Identity;

/// <summary>
/// The single-use, short-lived link behind "forgot your password" (ORB-A10). Not in
/// orbita-schema.dbml — same class of addition as InvitationToken and RefreshToken.
/// Global to the User, not tenant-scoped: a person requests this before any tenant
/// context exists, the same way login does. Only the hash of the token is ever
/// persisted, same as InvitationToken/RefreshToken.
/// </summary>
public sealed class PasswordResetToken : Entity
{
    private PasswordResetToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? usedAt)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
    }

    public Guid UserId { get; }

    public string TokenHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsValid(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public static PasswordResetToken Issue(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
        => new(Guid.NewGuid(), userId, tokenHash, now, now + lifetime, usedAt: null);

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
