using System.ComponentModel.DataAnnotations;
using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

public sealed record InviteTeamMemberRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    MemberRole Role);
