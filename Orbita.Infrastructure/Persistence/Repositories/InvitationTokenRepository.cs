using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class InvitationTokenRepository(OrbitaDbContext dbContext) : IInvitationTokenRepository
{
    public Task<InvitationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
        => dbContext.InvitationTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(InvitationToken token, CancellationToken cancellationToken)
        => await dbContext.InvitationTokens.AddAsync(token, cancellationToken);

    public Task InvalidateForMembershipAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken)
        => dbContext.InvitationTokens
            .Where(t => t.MembershipId == membershipId && t.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.UsedAt, now), cancellationToken);
}
