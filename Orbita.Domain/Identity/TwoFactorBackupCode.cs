using Orbita.Domain.Common;

namespace Orbita.Domain.Identity;

/// <summary>
/// A single-use recovery code for signing in when a person can't produce a TOTP code
/// (ORB-A11) — a lost phone, a broken authenticator app. Only the hash is ever
/// persisted, same as every other token in Identity. A batch is issued together when
/// two-factor is enabled or explicitly regenerated; each code is spent independently.
/// </summary>
public sealed class TwoFactorBackupCode : Entity
{
    private TwoFactorBackupCode(Guid id, Guid userId, string codeHash, DateTimeOffset createdAt, DateTimeOffset? usedAt)
        : base(id)
    {
        UserId = userId;
        CodeHash = codeHash;
        CreatedAt = createdAt;
        UsedAt = usedAt;
    }

    public Guid UserId { get; }

    public string CodeHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsed => UsedAt is not null;

    public static TwoFactorBackupCode Issue(Guid userId, string codeHash, DateTimeOffset now)
        => new(Guid.NewGuid(), userId, codeHash, now, usedAt: null);

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
