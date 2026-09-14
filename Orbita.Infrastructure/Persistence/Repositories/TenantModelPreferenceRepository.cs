using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class TenantModelPreferenceRepository(OrbitaDbContext dbContext) : ITenantModelPreferenceRepository
{
    public void Add(TenantModelPreference preference) => dbContext.TenantModelPreferences.Add(preference);

    public void Remove(TenantModelPreference preference) => dbContext.TenantModelPreferences.Remove(preference);

    public async Task<IReadOnlyList<TenantModelPreference>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
        => await dbContext.TenantModelPreferences
            .Where(preference => preference.TenantId == tenantId)
            .ToListAsync(cancellationToken);
}
