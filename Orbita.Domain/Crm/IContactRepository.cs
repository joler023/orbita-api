namespace Orbita.Domain.Crm;

public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Contact?> FindDuplicateAsync(
        Guid tenantId,
        string? phone,
        string? instagramUsername,
        Guid? exceptContactId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Contact>> SearchAsync(Guid tenantId, string? query, string? channel, CancellationToken cancellationToken);

    Task<int> CountByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Inbound message routing (ORB-B03): find-or-create by the channel's own identifier.</summary>
    Task<Contact?> FindByPhoneAsync(Guid tenantId, string phone, CancellationToken cancellationToken);

    Task<Contact?> FindByInstagramUserIdAsync(Guid tenantId, string instagramUserId, CancellationToken cancellationToken);

    Task AddAsync(Contact contact, CancellationToken cancellationToken);
}
