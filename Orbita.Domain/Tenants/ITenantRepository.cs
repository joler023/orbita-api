namespace Orbita.Domain.Tenants;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every active tenant's id. Safe to read with no ambient tenant because
    /// <c>tenants</c> is the one table that is not tenant-scoped — it *is* the tenant.
    /// Background workers use this to walk tenants and adopt each one's scope in turn
    /// (see <c>IKnowledgeDocumentRepository.ListPendingAsync</c>).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListActiveIdsAsync(CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
