namespace Orbita.Domain.Identity;

public interface IPasswordResetTokenRepository
{
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken);

    /// <summary>Used when a new reset is requested, so an older link stops working.</summary>
    Task InvalidateForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
