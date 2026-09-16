using Orbita.Domain.Common;

namespace Orbita.UnitTests.TestSupport;

/// <summary>
/// An <see cref="IUnitOfWork"/> that just runs what it is given.
///
/// The tenant scopes exist to put <c>app.tenant_id</c> on a real Postgres session, which
/// is meaningless without one — a unit test asserting that a scope was opened would only
/// be asserting that the production code calls a method it obviously calls.
/// <c>TenantIsolationTests</c> and the API tests are where that gets proven, against real
/// Row Level Security.
///
/// Mocking this instead is possible but awkward and quietly brittle: each generic scope
/// has to be stubbed per closed type, an un-stubbed one silently swallows the delegate
/// (the body under test never runs, and the test still passes for the wrong reason), and
/// every service that starts using a scope breaks every mock of it.
/// </summary>
internal sealed class PassThroughUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    /// <summary>How many times a read-write tenant scope ran to completion.</summary>
    public int TenantScopedSaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        return Task.CompletedTask;
    }

    public Task<TResult> QueryInTenantScopeAsync<TResult>(
        Func<CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken)
        => query(cancellationToken);

    public Task ExecuteInTenantScopeAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
        => operation(cancellationToken);

    public async Task ExecuteAndSaveInTenantScopeAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await operation(cancellationToken);

        TenantScopedSaveCount++;
    }

    public Task<TResult> QueryInUserScopeAsync<TResult>(
        Guid userId,
        Func<CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken)
        => query(cancellationToken);
}
