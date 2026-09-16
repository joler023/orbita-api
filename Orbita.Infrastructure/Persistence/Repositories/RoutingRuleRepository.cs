using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class RoutingRuleRepository(OrbitaDbContext dbContext) : IRoutingRuleRepository
{
    public async Task<IReadOnlyList<RoutingRule>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.RoutingRules
            .Where(rule => rule.TenantId == tenantId)
            .OrderBy(rule => rule.Position)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Tracked removes and adds rather than ExecuteDelete: those run outside the caller's
    /// transaction, so on this RLS'd table they would delete nothing — silently — and the
    /// "replace" would append a duplicate list instead.
    /// </summary>
    public async Task ReplaceAsync(Guid tenantId, IReadOnlyList<RoutingRule> rules, CancellationToken cancellationToken)
    {
        var existing = await dbContext.RoutingRules.Where(rule => rule.TenantId == tenantId).ToListAsync(cancellationToken);

        dbContext.RoutingRules.RemoveRange(existing);
        await dbContext.RoutingRules.AddRangeAsync(rules, cancellationToken);
    }
}
