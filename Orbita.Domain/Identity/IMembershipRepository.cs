namespace Orbita.Domain.Identity;

public interface IMembershipRepository
{
    Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Membership?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    /// <summary>All active memberships in the tenant, pending invitations included (ORB-A08).</summary>
    Task<IReadOnlyList<Membership>> GetActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Used to enforce "a tenant always keeps at least one Owner" (ORB-A08).</summary>
    Task<int> CountActiveByTenantAndRoleAsync(Guid tenantId, MemberRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Every organization this person actually belongs to, across tenants (ORB-A16).
    ///
    /// The one query in the codebase that deliberately crosses tenant boundaries, which
    /// is why it only runs inside <c>IUnitOfWork.QueryInUserScopeAsync</c> — outside it
    /// the Row Level Security policy on <c>memberships</c> returns nothing at all.
    ///
    /// Inactive and still-pending memberships are excluded: a removed member is not a
    /// member, and an unaccepted invitation is an offer, not an organization you are in.
    /// </summary>
    Task<IReadOnlyList<Membership>> ListAcceptedByUserAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(Membership membership, CancellationToken cancellationToken);
}
