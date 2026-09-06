namespace Orbita.Domain.Common;

/// <summary>
/// Commits everything added to repositories sharing the same underlying persistence
/// context in a single transaction. Exists because organization registration must
/// insert a Tenant, a User and a Membership atomically (ORB-A05) — one repository's
/// own SaveChanges is not enough once a use case spans more than one aggregate.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs a tenant-scoped read inside a transaction that first synchronizes
    /// `app.tenant_id`, so the Row Level Security policy on tenant-scoped tables
    /// (e.g. memberships) actually allows it — RLS session state set outside a
    /// transaction is not reliably visible to a query that follows on a pooled
    /// connection (see Orbita.Infrastructure.Persistence.UnitOfWork). Use this for
    /// any read of a tenant-scoped table that isn't immediately followed by
    /// SaveChangesAsync in the same flow — for example, checking a caller's role
    /// before authorizing a write (ORB-A07).
    /// </summary>
    Task<TResult> QueryInTenantScopeAsync<TResult>(
        Func<CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken);
}
