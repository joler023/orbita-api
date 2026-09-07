using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Identity;

public sealed record RequestPasswordResetRequest([Required, EmailAddress, MaxLength(320)] string Email);
