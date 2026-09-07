using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class TwoFactorBackupCodeRepository(OrbitaDbContext dbContext) : ITwoFactorBackupCodeRepository
{
    public Task<TwoFactorBackupCode?> GetByHashAsync(Guid userId, string codeHash, CancellationToken cancellationToken)
        => dbContext.TwoFactorBackupCodes.SingleOrDefaultAsync(c => c.UserId == userId && c.CodeHash == codeHash, cancellationToken);

    public async Task AddRangeAsync(IReadOnlyCollection<TwoFactorBackupCode> codes, CancellationToken cancellationToken)
        => await dbContext.TwoFactorBackupCodes.AddRangeAsync(codes, cancellationToken);

    public Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        => dbContext.TwoFactorBackupCodes
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, now), cancellationToken);
}
