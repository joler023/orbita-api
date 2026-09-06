using Orbita.Domain.Common;

namespace Orbita.Domain.Identity;

/// <summary>
/// Links a <see cref="User"/> to a tenant with a role. This is the first tenant-scoped
/// entity beyond the root Tenant itself, so it is the one that must carry TenantId and
/// be covered by the query filter + Row Level Security + tenant_id-first index isolation
/// (orbita-schema.dbml rule 1 / CLAUDE.md domain rule 1).
/// </summary>
public sealed class Membership : Entity
{
    private Membership(
        Guid id,
        Guid tenantId,
        Guid userId,
        MemberRole role,
        Guid? invitedBy,
        DateTimeOffset? invitedAt,
        DateTimeOffset? acceptedAt,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        InvitedBy = invitedBy;
        InvitedAt = invitedAt;
        AcceptedAt = acceptedAt;
        IsActive = isActive;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }

    public MemberRole Role { get; private set; }

    public Guid? InvitedBy { get; }

    public DateTimeOffset? InvitedAt { get; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Not yet accepted through the invitation link (ORB-A07).</summary>
    public bool IsPending => AcceptedAt is null;

    /// <summary>
    /// The membership created for the person who registers a new organization:
    /// self-accepted, role Owner, no inviter (ORB-A05).
    /// </summary>
    public static Membership CreateOwner(Guid tenantId, Guid userId, DateTimeOffset now) => new(
        Guid.NewGuid(),
        tenantId,
        userId,
        MemberRole.Owner,
        invitedBy: null,
        invitedAt: null,
        acceptedAt: now,
        isActive: true,
        createdAt: now);

    /// <summary>
    /// A pending invitation sent by an existing Owner/Admin (ORB-A07). Stays pending
    /// until <see cref="Accept"/> is called through a redeemed invitation token.
    /// </summary>
    public static Membership Invite(Guid tenantId, Guid userId, MemberRole role, Guid invitedBy, DateTimeOffset now) => new(
        Guid.NewGuid(),
        tenantId,
        userId,
        role,
        invitedBy,
        invitedAt: now,
        acceptedAt: null,
        isActive: true,
        createdAt: now);

    public void Accept(DateTimeOffset now) => AcceptedAt = now;

    /// <summary>Used both to revoke a pending invitation and to remove an active member.</summary>
    public void Deactivate() => IsActive = false;
}
