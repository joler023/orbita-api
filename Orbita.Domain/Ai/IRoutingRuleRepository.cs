namespace Orbita.Domain.Ai;

public interface IRoutingRuleRepository
{
    /// <summary>In evaluation order.</summary>
    Task<IReadOnlyList<RoutingRule>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the tenant's whole rule list. The screen edits an ordered list, and a
    /// reorder is a change to every row's position at once — doing it as one replacement
    /// is what keeps two positions from ever colliding mid-save.
    /// </summary>
    Task ReplaceAsync(Guid tenantId, IReadOnlyList<RoutingRule> rules, CancellationToken cancellationToken);
}
