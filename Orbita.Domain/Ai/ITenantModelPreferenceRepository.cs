namespace Orbita.Domain.Ai;

public interface ITenantModelPreferenceRepository
{
    void Add(TenantModelPreference preference);

    void Remove(TenantModelPreference preference);

    /// <summary>
    /// Every override a tenant has set. Read as a whole rather than one at a time because
    /// model selection happens on every single model call — including once per chunk while
    /// indexing a document — so the selector caches the tenant's full set instead of
    /// querying per call.
    /// </summary>
    Task<IReadOnlyList<TenantModelPreference>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
