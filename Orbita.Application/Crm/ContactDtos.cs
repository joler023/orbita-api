namespace Orbita.Application.Crm;

public sealed record ContactListItem(
    Guid Id,
    string DisplayName,
    string? Phone,
    string? InstagramUsername,
    string? Email,
    string Channel,
    DateTimeOffset UpdatedAt,
    string? StageName,
    decimal? Amount,
    string? AssignedToName);

public sealed record ContactDetail(
    Guid Id,
    string DisplayName,
    string? Phone,
    string? InstagramUsername,
    string? Email,
    string Channel,
    IReadOnlyDictionary<string, string> CustomFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ContactOpportunitySummary(
    Guid Id,
    Guid PipelineId,
    string PipelineName,
    Guid StageId,
    string StageName,
    string Title,
    decimal? Amount,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTimeOffset UpdatedAt);

public sealed record ContactFieldDefinitionSummary(
    Guid Id,
    string Key,
    string Label,
    string FieldType);

public sealed record CreateContactRequest(
    string DisplayName,
    string? Phone,
    string? InstagramUsername,
    string? Email,
    string? Channel,
    IReadOnlyDictionary<string, string>? CustomFields);

public sealed record UpdateContactRequest(
    string? DisplayName,
    string? Phone,
    string? InstagramUsername,
    string? Email,
    string? Channel,
    IReadOnlyDictionary<string, string>? CustomFields);

public sealed record CreateContactFieldRequest(string Key, string Label, string FieldType);
