namespace Orbita.Application.Identity;

public sealed class MembershipAlreadyExistsException(string email)
    : Exception($"'{email}' is already a member of, or already invited to, this organization.")
{
    public string Email { get; } = email;
}
