namespace Orbita.Application.Identity;

public sealed record RegisterOrganizationResult(
    Guid TenantId,
    string TenantSlug,
    string BusinessName,
    Guid UserId,
    string Email,
    string FullName);
