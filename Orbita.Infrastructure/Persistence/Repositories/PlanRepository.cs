using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Billing;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class PlanRepository(OrbitaDbContext dbContext) : IPlanRepository
{
    public async Task<IReadOnlyList<Plan>> GetAllActiveAsync(CancellationToken cancellationToken)
        => await dbContext.Plans.Where(p => p.IsActive).ToListAsync(cancellationToken);

    public Task<Plan?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Plans.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
}
