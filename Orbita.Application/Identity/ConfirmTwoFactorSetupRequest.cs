using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Identity;

public sealed record ConfirmTwoFactorSetupRequest([Required, StringLength(6, MinimumLength = 6)] string Code);
