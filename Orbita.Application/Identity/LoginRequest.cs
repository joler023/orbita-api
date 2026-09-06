using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Identity;

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MaxLength(200)] string Password);
