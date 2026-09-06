using Orbita.Domain.Common;

namespace Orbita.Domain.Identity;

/// <summary>
/// The single-use, expiring link behind a team invitation email (ORB-A07). Not in
/// orbita-schema.dbml — the schema models the invitation's lifecycle on Membership
/// itself (invited_at/accepted_at) but predates the secure redeemable token a real
/// email link needs. Only the hash of the token is ever persisted, same as
/// RefreshToken.
///
/// TenantId is denormalized from the Membership it belongs to on purpose: redeeming a
/// token starts with nothing but the raw value, no ambient tenant yet — this table
/// carries no isolation itself (it isn't tenant-scoped, same as refresh_tokens), so
/// reading it needs none, and it is what tells the accept flow which tenant to
/// establish before it can read the Membership behind it.
/// </summary>
public sealed class InvitationToken : Entity
{
    private InvitationToken(
        Guid id,
        Guid membershipId,
        Guid tenantId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? usedAt)
        : base(id)
    {
        MembershipId = membershipId;
        TenantId = tenantId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
    }

    public Guid MembershipId { get; }

    public Guid TenantId { get; }

    public string TokenHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsValid(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public static InvitationToken Issue(Guid membershipId, Guid tenantId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
        => new(Guid.NewGuid(), membershipId, tenantId, tokenHash, now, now + lifetime, usedAt: null);

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
