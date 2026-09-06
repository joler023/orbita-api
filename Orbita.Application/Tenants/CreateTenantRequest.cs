using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Tenants;

public sealed record CreateTenantRequest(
    [Required, MaxLength(64)] string Slug,
    [Required, MaxLength(200)] string Name,
    [MaxLength(2)] string? CountryCode = null,
    [MaxLength(64)] string? Timezone = null,
    [MaxLength(10)] string? Locale = null);
