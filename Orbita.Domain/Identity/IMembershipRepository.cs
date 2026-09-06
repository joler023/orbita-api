namespace Orbita.Domain.Identity;

public interface IMembershipRepository
{
    Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Membership?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task AddAsync(Membership membership, CancellationToken cancellationToken);
}
