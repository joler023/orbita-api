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

    /// <summary>
    /// Runs a tenant-scoped write that does not go through the change tracker — an
    /// <c>ExecuteDelete</c>/<c>ExecuteUpdate</c> bulk operation — inside a transaction
    /// that first synchronizes `app.tenant_id`, and commits it.
    ///
    /// Bulk operations execute immediately rather than waiting for
    /// <see cref="SaveChangesAsync"/>, so on a tenant-scoped table they run with no
    /// `app.tenant_id` set unless wrapped like this — and Row Level Security then matches
    /// zero rows, so the delete silently affects nothing. That failure is invisible: no
    /// error, just stale rows. ORB-C02's reindex is where it was first caught.
    ///
    /// The same reasoning is why <c>IRefreshTokenRepository.RevokeFamilyAsync</c> can use
    /// a bare bulk update: `refresh_tokens` is deliberately not tenant-scoped.
    /// </summary>
    Task ExecuteInTenantScopeAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a read scoped to one <em>person</em> rather than to one organization, by
    /// synchronizing `app.user_id` the way <see cref="QueryInTenantScopeAsync{TResult}"/>
    /// synchronizes `app.tenant_id`.
    ///
    /// <para>This exists for exactly one question: "which organizations does the person
    /// who just signed in belong to?" (ORB-A16). It cannot be answered tenant-scoped —
    /// the answer is what tells you the tenant — and it cannot be answered unscoped
    /// either, because the Row Level Security policy on <c>memberships</c> returns zero
    /// rows, silently, when no tenant is set.</para>
    ///
    /// <para><b>The user id must come from the authenticated session, never from the
    /// request.</b> It is what the database will trust: passing an id a caller supplied
    /// would let anyone read anyone's memberships. Callers take it from the JWT's
    /// <c>sub</c> claim.</para>
    ///
    /// Like the tenant one, the value is set with SET LOCAL semantics, so it is gone when
    /// the transaction ends and a pooled connection can never carry it into whatever runs
    /// next.
    /// </summary>
    Task<TResult> QueryInUserScopeAsync<TResult>(
        Guid userId,
        Func<CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken);
}
