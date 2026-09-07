namespace Orbita.Domain.Identity;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every still-active token sharing <paramref name="familyId"/> — the
    /// response to detecting a revoked token being reused (ORB-A06).
    /// </summary>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every still-active token for the user, across every family — used when
    /// a password reset succeeds, since "resetting invalidates all active sessions" (ORB-A10)
    /// means all of them, not just the one family behind the current session.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
