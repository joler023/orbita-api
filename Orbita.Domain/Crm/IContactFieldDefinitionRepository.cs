namespace Orbita.Domain.Crm;

public interface IContactFieldDefinitionRepository
{
    Task<IReadOnlyList<ContactFieldDefinition>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<ContactFieldDefinition?> GetByKeyAsync(Guid tenantId, string key, CancellationToken cancellationToken);

    Task AddAsync(ContactFieldDefinition definition, CancellationToken cancellationToken);
}
