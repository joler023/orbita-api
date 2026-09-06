namespace Orbita.Application.Identity;

public sealed class EmailAlreadyRegisteredException(string email)
    : Exception($"A user with email '{email}' already exists.")
{
    public string Email { get; } = email;
}
