using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

public sealed record TeamInvitationSummary(
    Guid MembershipId,
    string Email,
    MemberRole Role,
    DateTimeOffset InvitedAt,
    DateTimeOffset ExpiresAt);
