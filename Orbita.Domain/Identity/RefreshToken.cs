using Orbita.Domain.Common;

namespace Orbita.Domain.Identity;

/// <summary>
/// A single link in a rotating chain of refresh tokens issued to a User (ORB-A06). Not
/// in orbita-schema.dbml — the schema predates session/token storage — modeled here
/// because rotation with reuse detection needs a server-side record of every issued
/// token, not just the current one.
///
/// Only a hash of the token is ever stored (<see cref="TokenHash"/>); the raw value
/// lives only in the httpOnly cookie handed to the client. All tokens issued from one
/// login, through however many rotations, share <see cref="FamilyId"/> — presenting a
/// token that was already revoked is the signal that a family may have been stolen, and
/// the whole family gets revoked in response, not just the reused token.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? revokedAt,
        Guid? replacedByTokenId)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        ReplacedByTokenId = replacedByTokenId;
    }

    public Guid UserId { get; }

    public string TokenHash { get; }

    public Guid FamilyId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Starts a brand-new family — the first token issued at login.</summary>
    public static RefreshToken IssueNewFamily(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
        => new(Guid.NewGuid(), userId, tokenHash, Guid.NewGuid(), now, now + lifetime, revokedAt: null, replacedByTokenId: null);

    /// <summary>Continues an existing family — issued when rotating a still-valid token.</summary>
    public static RefreshToken IssueInFamily(Guid userId, string tokenHash, Guid familyId, DateTimeOffset now, TimeSpan lifetime)
        => new(Guid.NewGuid(), userId, tokenHash, familyId, now, now + lifetime, revokedAt: null, replacedByTokenId: null);

    public void RevokeReplacedBy(Guid newTokenId, DateTimeOffset now)
    {
        RevokedAt = now;
        ReplacedByTokenId = newTokenId;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt = now;
}
