using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Identity;

/// <summary>Requires re-entering the password — a valid session cookie alone must not be enough to turn off 2FA.</summary>
public sealed record DisableTwoFactorRequest([Required] string CurrentPassword);
