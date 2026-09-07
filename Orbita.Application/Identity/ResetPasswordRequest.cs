using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Identity;

public sealed record ResetPasswordRequest(
    [Required] string Token,
    [Required, MinLength(8), MaxLength(200)] string NewPassword);
