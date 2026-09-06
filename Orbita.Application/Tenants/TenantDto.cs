namespace Orbita.Application.Tenants;

public sealed record TenantDto(
    Guid Id,
    string Slug,
    string Name,
    string CountryCode,
    string Timezone,
    string Locale,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
