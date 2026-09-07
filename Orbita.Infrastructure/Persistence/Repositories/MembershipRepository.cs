using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class MembershipRepository(OrbitaDbContext dbContext) : IMembershipRepository
{
    public Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Memberships.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<Membership?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
        => dbContext.Memberships.SingleOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Membership>> GetActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.Memberships
            .Where(m => m.TenantId == tenantId && m.IsActive)
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveByTenantAndRoleAsync(Guid tenantId, MemberRole role, CancellationToken cancellationToken)
        => dbContext.Memberships.CountAsync(m => m.TenantId == tenantId && m.IsActive && m.Role == role, cancellationToken);

    public async Task AddAsync(Membership membership, CancellationToken cancellationToken)
        => await dbContext.Memberships.AddAsync(membership, cancellationToken);
}
