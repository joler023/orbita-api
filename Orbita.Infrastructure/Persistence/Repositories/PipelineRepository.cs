using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Crm;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class PipelineRepository(OrbitaDbContext dbContext) : IPipelineRepository
{
    public Task<Pipeline?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Pipelines.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Pipeline>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.Pipelines
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => dbContext.Pipelines.CountAsync(p => p.TenantId == tenantId, cancellationToken);

    public async Task AddAsync(Pipeline pipeline, CancellationToken cancellationToken)
        => await dbContext.Pipelines.AddAsync(pipeline, cancellationToken);

    public void Remove(Pipeline pipeline)
        => dbContext.Pipelines.Remove(pipeline);
}
