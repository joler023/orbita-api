namespace Orbita.Application.Ai;

/// <summary>
/// Drops a tenant's cached model preferences so the next call re-reads them.
///
/// A port rather than a direct dependency on the cache because caching is an
/// infrastructure concern: <see cref="ModelPreferenceService"/> knows that a write must
/// invalidate, not what it is invalidating in.
/// </summary>
public interface IModelPreferenceCacheInvalidator
{
    void Invalidate(Guid tenantId);
}
