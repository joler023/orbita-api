using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Orbita.Application.Identity;

namespace Orbita.Infrastructure.Identity;

/// <summary>
/// Argon2id, as mandated by orbita-schema.dbml (users.password_hash note) and
/// ADR-011. Produces a self-describing PHC-like string so parameters can change later
/// without breaking verification of hashes issued under the old ones.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemorySizeKb = 65536; // 64 MB
    private const int Iterations = 3;
    private const int DegreeOfParallelism = 1;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeHash(password, salt, HashSize);

        return $"$argon2id$v=19$m={MemorySizeKb},t={Iterations},p={DegreeOfParallelism}" +
            $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string passwordHash, string password)
    {
        var parts = passwordHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id")
        {
            return false;
        }

        var parameters = ParseParameters(parts[2]);
        var salt = Convert.FromBase64String(parts[3]);
        var expectedHash = Convert.FromBase64String(parts[4]);

        var actualHash = ComputeHash(
            password,
            salt,
            expectedHash.Length,
            parameters.MemorySizeKb,
            parameters.Iterations,
            parameters.DegreeOfParallelism);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] ComputeHash(
        string password,
        byte[] salt,
        int hashSize,
        int memorySizeKb = MemorySizeKb,
        int iterations = Iterations,
        int degreeOfParallelism = DegreeOfParallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = degreeOfParallelism,
            Iterations = iterations,
            MemorySize = memorySizeKb,
        };

        return argon2.GetBytes(hashSize);
    }

    private static (int MemorySizeKb, int Iterations, int DegreeOfParallelism) ParseParameters(string encoded)
    {
        // encoded looks like "m=65536,t=3,p=1"
        var values = encoded
            .Split(',')
            .Select(part => part.Split('='))
            .ToDictionary(kv => kv[0], kv => int.Parse(kv[1]));

        return (values["m"], values["t"], values["p"]);
    }
}
