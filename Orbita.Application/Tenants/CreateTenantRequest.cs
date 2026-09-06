using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Tenants;

public sealed record CreateTenantRequest(
    [property: Required, MaxLength(64)] string Slug,
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(2)] string? CountryCode = null,
    [property: MaxLength(64)] string? Timezone = null,
    [property: MaxLength(10)] string? Locale = null);
