using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Common;

namespace Orbita.Infrastructure.Persistence;

/// <summary>
/// Wraps OrbitaDbContext.SaveChangesAsync so every write goes through one choke point
/// that synchronizes the acting tenant into Postgres (`app.tenant_id`), which is what
/// the Row Level Security policy on `memberships` checks (orbita-schema.dbml /
/// ADR-004).
///
/// The synchronization and the save (or, for <see cref="QueryInTenantScopeAsync{TResult}"/>,
/// the read) must share one physical connection, or the session variable set by one
/// command can silently not be there for the next: EF Core does not guarantee that two
/// unrelated calls against a pooled connection reuse the same underlying connection.
/// Opening an explicit transaction first pins one connection for both, and using
/// `set_config(..., is_local: true)` (SET LOCAL semantics) ties the value to that same
/// transaction, so it is automatically gone once the transaction ends — a pooled
/// connection can never carry a stale tenant into whatever runs on it next.
/// </summary>
public sealed class UnitOfWork(OrbitaDbContext dbContext, ITenantContext tenantContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await SynchronizeTenantSessionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TResult> QueryInTenantScopeAsync<TResult>(
        Func<CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await SynchronizeTenantSessionAsync(cancellationToken);
        var result = await query(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private Task SynchronizeTenantSessionAsync(CancellationToken cancellationToken)
    {
        var tenantIdValue = tenantContext.TenantId?.ToString() ?? string.Empty;
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tenantIdValue}, true);",
            cancellationToken);
    }
}
