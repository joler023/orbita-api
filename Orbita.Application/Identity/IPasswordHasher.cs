namespace Orbita.Application.Identity;

/// <summary>
/// Port for password hashing (ADR-011: Identity is homegrown, so this is our own seam
/// instead of a managed provider's). Infrastructure implements it with Argon2id.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
