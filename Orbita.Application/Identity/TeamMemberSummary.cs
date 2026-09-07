using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

public sealed record TeamMemberSummary(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string FullName,
    MemberRole Role,
    bool IsPending,
    DateTimeOffset CreatedAt);
