namespace Orbita.Domain.Tenants;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The named tenants, for callers that already established <em>which</em> ones they
    /// are allowed to see. <c>tenants</c> carries no tenant_id and so has no Row Level
    /// Security of its own: the ids passed in are the whole access check, and they must
    /// come from something that was itself authorized (ORB-A16 passes the ids of the
    /// caller's own memberships).
    /// </summary>
    Task<IReadOnlyList<Tenant>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
