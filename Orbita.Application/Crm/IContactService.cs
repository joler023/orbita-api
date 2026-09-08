namespace Orbita.Application.Crm;

public interface IContactService
{
    Task<IReadOnlyList<ContactListItem>> SearchAsync(
        Guid tenantId,
        Guid callerUserId,
        string? query,
        string? channel,
        CancellationToken cancellationToken);

    Task<ContactDetail> GetAsync(Guid tenantId, Guid callerUserId, Guid contactId, CancellationToken cancellationToken);

    Task<ContactDetail> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        CreateContactRequest request,
        CancellationToken cancellationToken);

    Task<ContactDetail> UpdateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid contactId,
        UpdateContactRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactOpportunitySummary>> ListOpportunitiesAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid contactId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactFieldDefinitionSummary>> ListFieldsAsync(
        Guid tenantId,
        Guid callerUserId,
        CancellationToken cancellationToken);

    Task<ContactFieldDefinitionSummary> CreateFieldAsync(
        Guid tenantId,
        Guid callerUserId,
        CreateContactFieldRequest request,
        CancellationToken cancellationToken);
}
