using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository(OrbitaDbContext dbContext) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
        => dbContext.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
        => await dbContext.RefreshTokens.AddAsync(token, cancellationToken);

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
        => dbContext.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);
}
