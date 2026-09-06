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
}
