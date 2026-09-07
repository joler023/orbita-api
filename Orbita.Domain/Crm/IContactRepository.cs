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

    Task AddAsync(Contact contact, CancellationToken cancellationToken);
}
