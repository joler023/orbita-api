using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class MembershipRepository(OrbitaDbContext dbContext) : IMembershipRepository
{
    public async Task AddAsync(Membership membership, CancellationToken cancellationToken)
        => await dbContext.Memberships.AddAsync(membership, cancellationToken);
}
