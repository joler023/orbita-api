namespace Orbita.Domain.Crm;

public interface IPipelineRepository
{
    Task<Pipeline?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Pipeline>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<int> CountByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(Pipeline pipeline, CancellationToken cancellationToken);

    void Remove(Pipeline pipeline);
}
