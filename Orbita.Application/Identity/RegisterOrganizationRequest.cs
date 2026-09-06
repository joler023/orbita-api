using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Identity;

/// <summary>
/// The whole registration form (ORB-A05 / Guía de pantallas 1.1): business name, the
/// registrant's own name, email and password. Nothing else — every extra field loses
/// people at signup.
/// </summary>
public sealed record RegisterOrganizationRequest(
    [Required, MaxLength(200)] string BusinessName,
    [Required, MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(8), MaxLength(200)] string Password);
