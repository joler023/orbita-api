namespace Orbita.Application.Identity;

public sealed record AcceptInvitationResult(
    Guid TenantId,
    string TenantSlug,
    Guid UserId,
    string Email,
    string FullName);
