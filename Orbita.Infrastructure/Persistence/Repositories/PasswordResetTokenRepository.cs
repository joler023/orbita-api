using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository(OrbitaDbContext dbContext) : IPasswordResetTokenRepository
{
    public Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
        => dbContext.PasswordResetTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken)
        => await dbContext.PasswordResetTokens.AddAsync(token, cancellationToken);

    public Task InvalidateForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        => dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.UsedAt, now), cancellationToken);
}
