namespace Orbita.Domain.Identity;

public interface ITwoFactorBackupCodeRepository
{
    Task<TwoFactorBackupCode?> GetByHashAsync(Guid userId, string codeHash, CancellationToken cancellationToken);

    Task AddRangeAsync(IReadOnlyCollection<TwoFactorBackupCode> codes, CancellationToken cancellationToken);

    /// <summary>Used when regenerating or disabling, so old codes stop working.</summary>
    Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
