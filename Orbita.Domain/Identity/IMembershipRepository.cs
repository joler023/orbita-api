namespace Orbita.Domain.Identity;

public interface IMembershipRepository
{
    Task AddAsync(Membership membership, CancellationToken cancellationToken);
}
