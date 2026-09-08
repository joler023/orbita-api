using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Orbita.Application.Channels;
using Orbita.Infrastructure.Persistence;

namespace Orbita.Infrastructure.Channels;

/// <summary>
/// Stands in for AWS Secrets Manager + KMS until they are wired up (ORB-B01), the same
/// "swap the Infrastructure implementation later" pattern as
/// DataProtectionUserSecretProtector. The token is encrypted with ASP.NET Core Data
/// Protection and stored in <c>channel_credentials</c>, addressed by an opaque
/// <c>local://{id}</c> reference that ChannelAccount.CredentialsRef persists exactly
/// where a Secrets Manager ARN would go. Deviation from orbita-schema.dbml's rule 7
/// ("nunca vive aquí"): the ciphertext does live in Postgres for now — never the
/// plaintext, and never in any log line.
///
/// <para>Writes commit immediately through raw SQL instead of the change tracker, so
/// this behaves like the external store it replaces (a Secrets Manager PUT is not part
/// of the caller's database transaction either) and never flushes whatever else the
/// calling service has staged on the shared DbContext.</para>
/// </summary>
public sealed class DataProtectionChannelCredentialStore : IChannelCredentialStore
{
    public const string ReferencePrefix = "local://";
    private const string Purpose = "Orbita.ChannelCredential.v1";

    private readonly OrbitaDbContext _dbContext;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public DataProtectionChannelCredentialStore(OrbitaDbContext dbContext, IDataProtectionProvider provider, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _protector = provider.CreateProtector(Purpose);
        _timeProvider = timeProvider;
    }

    public async Task<string> StoreAsync(string plaintextSecret, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var ciphertext = _protector.Protect(plaintextSecret);
        var now = _timeProvider.GetUtcNow();

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO channel_credentials (id, ciphertext, created_at) VALUES ({id}, {ciphertext}, {now})",
            cancellationToken);

        return ToReference(id);
    }

    public async Task<string> GetAsync(string credentialsRef, CancellationToken cancellationToken)
    {
        var id = ParseReference(credentialsRef);
        var credential = await _dbContext.ChannelCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("No credential is stored for that reference.");

        return _protector.Unprotect(credential.Ciphertext);
    }

    public async Task DeleteAsync(string credentialsRef, CancellationToken cancellationToken)
    {
        var id = ParseReference(credentialsRef);
        await _dbContext.ChannelCredentials
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string ToReference(Guid id) => $"{ReferencePrefix}{id:D}";

    private static Guid ParseReference(string credentialsRef)
    {
        if (credentialsRef.StartsWith(ReferencePrefix, StringComparison.Ordinal)
            && Guid.TryParse(credentialsRef.AsSpan(ReferencePrefix.Length), out var id))
        {
            return id;
        }

        throw new InvalidOperationException("The credentials reference is not a local reference — is a different credential store configured?");
    }
}
