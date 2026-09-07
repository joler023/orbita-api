namespace Orbita.Domain.Identity;

public interface IMembershipRepository
{
    Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Membership?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    /// <summary>All active memberships in the tenant, pending invitations included (ORB-A08).</summary>
    Task<IReadOnlyList<Membership>> GetActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Used to enforce "a tenant always keeps at least one Owner" (ORB-A08).</summary>
    Task<int> CountActiveByTenantAndRoleAsync(Guid tenantId, MemberRole role, CancellationToken cancellationToken);

    Task AddAsync(Membership membership, CancellationToken cancellationToken);
}
