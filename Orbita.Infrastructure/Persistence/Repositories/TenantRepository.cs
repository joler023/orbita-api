using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class TenantRepository(OrbitaDbContext dbContext) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Tenants.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => ids.Count == 0
            ? []
            : await dbContext.Tenants.Where(tenant => ids.Contains(tenant.Id)).ToListAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
        => dbContext.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
        => await dbContext.Tenants.AddAsync(tenant, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => dbContext.SaveChangesAsync(cancellationToken);
}
