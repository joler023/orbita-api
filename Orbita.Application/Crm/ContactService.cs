using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;

namespace Orbita.Application.Crm;

public sealed class ContactService(
    IContactRepository contactRepository,
    IContactFieldDefinitionRepository fieldRepository,
    IOpportunityRepository opportunityRepository,
    IPipelineRepository pipelineRepository,
    IPipelineStageRepository stageRepository,
    IUserRepository userRepository,
    ITenantAuthorizationService authorizationService,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IContactService
{
    private const int SearchLimit = 100;

    public async Task<IReadOnlyList<ContactListItem>> SearchAsync(
        Guid tenantId,
        Guid callerUserId,
        string? query,
        string? channel,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewContacts, cancellationToken);
        var contacts = await unitOfWork.QueryInTenantScopeAsync(
            ct => contactRepository.SearchAsync(tenantId, query, channel, ct),
            cancellationToken);

        var limited = contacts.Take(SearchLimit).ToArray();
        var deals = await unitOfWork.QueryInTenantScopeAsync(
            ct => opportunityRepository.GetByContactIdsAsync(limited.Select(c => c.Id).ToArray(), ct),
            cancellationToken);

        return await ToListItemsAsync(limited, deals, cancellationToken);
    }

    public async Task<ContactDetail> GetAsync(Guid tenantId, Guid callerUserId, Guid contactId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewContacts, cancellationToken);
        var contact = await RequireContactAsync(tenantId, contactId, cancellationToken);
        return ToDetail(contact);
    }

    public async Task<ContactDetail> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        CreateContactRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageContacts, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var contact = Contact.Create(
            tenantId,
            request.DisplayName,
            now,
            request.Phone,
            request.InstagramUsername,
            request.Email,
            request.Channel);

        await EnsureUniqueAsync(tenantId, contact, exceptContactId: null, cancellationToken);

        if (request.CustomFields is not null)
        {
            contact.ReplaceCustomFields(request.CustomFields, now);
        }

        await contactRepository.AddAsync(contact, cancellationToken);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "contact.created",
            nameof(Contact),
            contact.Id,
            new { contact.DisplayName, contact.Channel },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDetail(contact);
    }

    public async Task<ContactDetail> UpdateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid contactId,
        UpdateContactRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageContacts, cancellationToken);
        var contact = await RequireContactAsync(tenantId, contactId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (request.DisplayName is not null
            || request.Phone is not null
            || request.InstagramUsername is not null
            || request.Email is not null
            || request.Channel is not null)
        {
            contact.UpdateIdentity(
                request.DisplayName ?? contact.DisplayName,
                request.Phone ?? contact.Phone,
                request.InstagramUsername ?? contact.InstagramUsername,
                request.Email ?? contact.Email,
                request.Channel ?? contact.Channel,
                now);
        }

        if (request.CustomFields is not null)
        {
            contact.ReplaceCustomFields(request.CustomFields, now);
        }

        await EnsureUniqueAsync(tenantId, contact, contact.Id, cancellationToken);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "contact.updated",
            nameof(Contact),
            contact.Id,
            new { contact.DisplayName, contact.Channel },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDetail(contact);
    }

    public async Task<IReadOnlyList<ContactOpportunitySummary>> ListOpportunitiesAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid contactId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewContacts, cancellationToken);
        await RequireContactAsync(tenantId, contactId, cancellationToken);
        var deals = await unitOfWork.QueryInTenantScopeAsync(
            ct => opportunityRepository.GetByContactAsync(contactId, ct),
            cancellationToken);

        return await ToOpportunitySummariesAsync(deals, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactFieldDefinitionSummary>> ListFieldsAsync(
        Guid tenantId,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewContacts, cancellationToken);
        var fields = await unitOfWork.QueryInTenantScopeAsync(
            ct => fieldRepository.GetByTenantAsync(tenantId, ct),
            cancellationToken);

        return fields.Select(ToFieldSummary).ToArray();
    }

    public async Task<ContactFieldDefinitionSummary> CreateFieldAsync(
        Guid tenantId,
        Guid callerUserId,
        CreateContactFieldRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageContacts, cancellationToken);
        if (!Enum.TryParse<ContactFieldType>(request.FieldType, ignoreCase: true, out var fieldType))
        {
            throw new ArgumentException("Field type must be Text, Number, or Date.", nameof(request));
        }

        var existing = await unitOfWork.QueryInTenantScopeAsync(
            ct => fieldRepository.GetByKeyAsync(tenantId, request.Key, ct),
            cancellationToken);
        if (existing is not null)
        {
            throw new ContactFieldAlreadyExistsException();
        }

        var definition = ContactFieldDefinition.Create(
            tenantId,
            request.Key,
            request.Label,
            fieldType,
            timeProvider.GetUtcNow());
        await fieldRepository.AddAsync(definition, cancellationToken);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "contact_field.created",
            nameof(ContactFieldDefinition),
            definition.Id,
            new { definition.Key, definition.Label },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToFieldSummary(definition);
    }

    private async Task EnsureUniqueAsync(
        Guid tenantId,
        Contact contact,
        Guid? exceptContactId,
        CancellationToken cancellationToken)
    {
        var duplicate = await unitOfWork.QueryInTenantScopeAsync(
            ct => contactRepository.FindDuplicateAsync(
                tenantId,
                contact.Phone,
                contact.InstagramUsername,
                exceptContactId,
                ct),
            cancellationToken);

        if (duplicate is not null)
        {
            throw new ContactAlreadyExistsException();
        }
    }

    private async Task<Contact> RequireContactAsync(Guid tenantId, Guid contactId, CancellationToken cancellationToken)
    {
        var contact = await unitOfWork.QueryInTenantScopeAsync(
            ct => contactRepository.GetByIdAsync(contactId, ct),
            cancellationToken);

        if (contact is null || contact.TenantId != tenantId)
        {
            throw new ContactNotFoundException();
        }

        return contact;
    }

    private async Task<IReadOnlyList<ContactListItem>> ToListItemsAsync(
        IReadOnlyList<Contact> contacts,
        IReadOnlyList<Opportunity> deals,
        CancellationToken cancellationToken)
    {
        var latestByContact = deals
            .GroupBy(deal => deal.ContactId)
            .ToDictionary(group => group.Key!.Value, group => group.MaxBy(deal => deal.UpdatedAt)!);
        var names = await LoadAssigneeNamesAsync(latestByContact.Values.ToArray(), cancellationToken);
        var stages = await LoadStagesAsync(latestByContact.Values.Select(deal => deal.StageId).Distinct().ToArray(), cancellationToken);

        return contacts
            .Select(contact =>
            {
                latestByContact.TryGetValue(contact.Id, out var deal);
                var stageName = deal is not null && stages.TryGetValue(deal.StageId, out var stage)
                    ? stage.Name
                    : null;
                var assigneeName = deal?.AssignedToUserId is { } assigneeId && names.TryGetValue(assigneeId, out var name)
                    ? name
                    : null;
                return new ContactListItem(
                    contact.Id,
                    contact.DisplayName,
                    contact.Phone,
                    contact.InstagramUsername,
                    contact.Email,
                    contact.Channel,
                    contact.UpdatedAt,
                    stageName,
                    deal?.Amount,
                    assigneeName);
            })
            .ToArray();
    }

    private async Task<IReadOnlyList<ContactOpportunitySummary>> ToOpportunitySummariesAsync(
        IReadOnlyList<Opportunity> deals,
        CancellationToken cancellationToken)
    {
        var names = await LoadAssigneeNamesAsync(deals, cancellationToken);
        var stages = await LoadStagesAsync(deals.Select(deal => deal.StageId).Distinct().ToArray(), cancellationToken);
        var pipelines = await LoadPipelinesAsync(deals.Select(deal => deal.PipelineId).Distinct().ToArray(), cancellationToken);

        return deals
            .OrderByDescending(deal => deal.UpdatedAt)
            .Select(deal => new ContactOpportunitySummary(
                deal.Id,
                deal.PipelineId,
                pipelines.TryGetValue(deal.PipelineId, out var pipeline) ? pipeline.Name : string.Empty,
                deal.StageId,
                stages.TryGetValue(deal.StageId, out var stage) ? stage.Name : string.Empty,
                deal.Title,
                deal.Amount,
                deal.AssignedToUserId,
                deal.AssignedToUserId is { } assigneeId && names.TryGetValue(assigneeId, out var name) ? name : null,
                deal.UpdatedAt))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadAssigneeNamesAsync(
        IReadOnlyList<Opportunity> opportunities,
        CancellationToken cancellationToken)
    {
        var ids = opportunities
            .Select(opportunity => opportunity.AssignedToUserId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await userRepository.GetByIdsAsync(ids, cancellationToken);
        return users.ToDictionary(user => user.Id, user => user.FullName);
    }

    private async Task<IReadOnlyDictionary<Guid, PipelineStage>> LoadStagesAsync(
        IReadOnlyList<Guid> stageIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, PipelineStage>();
        foreach (var stageId in stageIds)
        {
            var stage = await unitOfWork.QueryInTenantScopeAsync(
                ct => stageRepository.GetByIdAsync(stageId, ct),
                cancellationToken);
            if (stage is not null)
            {
                result[stage.Id] = stage;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, Pipeline>> LoadPipelinesAsync(
        IReadOnlyList<Guid> pipelineIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Pipeline>();
        foreach (var pipelineId in pipelineIds)
        {
            var pipeline = await unitOfWork.QueryInTenantScopeAsync(
                ct => pipelineRepository.GetByIdAsync(pipelineId, ct),
                cancellationToken);
            if (pipeline is not null)
            {
                result[pipeline.Id] = pipeline;
            }
        }

        return result;
    }

    private static ContactDetail ToDetail(Contact contact)
        => new(
            contact.Id,
            contact.DisplayName,
            contact.Phone,
            contact.InstagramUsername,
            contact.Email,
            contact.Channel,
            new Dictionary<string, string>(contact.CustomFields, StringComparer.OrdinalIgnoreCase),
            contact.CreatedAt,
            contact.UpdatedAt);

    private static ContactFieldDefinitionSummary ToFieldSummary(ContactFieldDefinition definition)
        => new(definition.Id, definition.Key, definition.Label, definition.FieldType.ToString());
}
