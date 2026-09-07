using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Crm;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class ContactRepository(OrbitaDbContext dbContext) : IContactRepository
{
    public Task<Contact?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Contacts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Contact?> FindDuplicateAsync(
        Guid tenantId,
        string? phone,
        string? instagramUsername,
        Guid? exceptContactId,
        CancellationToken cancellationToken)
    {
        if (phone is null && instagramUsername is null)
        {
            return Task.FromResult<Contact?>(null);
        }

        return dbContext.Contacts.FirstOrDefaultAsync(
            contact =>
                contact.TenantId == tenantId
                && (exceptContactId == null || contact.Id != exceptContactId)
                && (
                    (phone != null && contact.Phone == phone)
                    || (instagramUsername != null && contact.InstagramUsername == instagramUsername)),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Contact>> SearchAsync(
        Guid tenantId,
        string? query,
        string? channel,
        CancellationToken cancellationToken)
    {
        var contacts = dbContext.Contacts.Where(contact => contact.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(channel))
        {
            var normalized = channel.Trim().ToLowerInvariant();
            contacts = contacts.Where(contact => contact.Channel == normalized);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var like = $"%{term}%";
            contacts = contacts.Where(contact =>
                EF.Functions.ILike(contact.DisplayName, like)
                || (contact.Phone != null && EF.Functions.ILike(contact.Phone, like))
                || (contact.InstagramUsername != null && EF.Functions.ILike(contact.InstagramUsername, like))
                || (contact.Email != null && EF.Functions.ILike(contact.Email, like)));
        }

        return await contacts
            .OrderBy(contact => contact.DisplayName)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => dbContext.Contacts.CountAsync(contact => contact.TenantId == tenantId, cancellationToken);

    public async Task AddAsync(Contact contact, CancellationToken cancellationToken)
        => await dbContext.Contacts.AddAsync(contact, cancellationToken);
}
