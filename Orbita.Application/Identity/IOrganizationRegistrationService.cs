namespace Orbita.Application.Identity;

public interface IOrganizationRegistrationService
{
    /// <exception cref="EmailAlreadyRegisteredException">The email is already taken.</exception>
    Task<RegisterOrganizationResult> RegisterAsync(RegisterOrganizationRequest request, CancellationToken cancellationToken);
}
