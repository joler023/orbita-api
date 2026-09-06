namespace Orbita.Domain.Identity;

public interface IInvitationTokenRepository
{
    Task<InvitationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task AddAsync(InvitationToken token, CancellationToken cancellationToken);

    /// <summary>Used when resending or revoking, so an older link stops working.</summary>
    Task InvalidateForMembershipAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken);
}
