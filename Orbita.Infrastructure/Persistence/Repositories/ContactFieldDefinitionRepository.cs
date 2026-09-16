using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Crm;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class ContactFieldDefinitionRepository(OrbitaDbContext dbContext) : IContactFieldDefinitionRepository
{
    public async Task<IReadOnlyList<ContactFieldDefinition>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
        => await dbContext.ContactFieldDefinitions
            .Where(field => field.TenantId == tenantId)
            .OrderBy(field => field.Label)
            .ToListAsync(cancellationToken);

    public Task<ContactFieldDefinition?> GetByKeyAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return dbContext.ContactFieldDefinitions.SingleOrDefaultAsync(
            field => field.TenantId == tenantId && field.Key == normalized,
            cancellationToken);
    }

    public async Task AddAsync(ContactFieldDefinition definition, CancellationToken cancellationToken)
        => await dbContext.ContactFieldDefinitions.AddAsync(definition, cancellationToken);
}
