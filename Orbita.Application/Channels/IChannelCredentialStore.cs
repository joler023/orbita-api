namespace Orbita.Application.Channels;

/// <summary>
/// Where channel access tokens actually live (ORB-B01). orbita-schema.dbml's rule 7:
/// channel_accounts.credentials_ref is a Secrets Manager ARN and the token is
/// KMS-encrypted — the token never sits in Postgres in the clear and never appears in
/// logs. The production implementation is AWS Secrets Manager; until that exists the
/// Infrastructure stand-in keeps a Data Protection-encrypted ciphertext in a separate
/// table and hands back an opaque <c>local://</c> reference, so nothing that calls
/// this port changes when the real store is wired in.
/// </summary>
public interface IChannelCredentialStore
{
    /// <returns>An opaque reference to persist in ChannelAccount.CredentialsRef.</returns>
    Task<string> StoreAsync(string plaintextSecret, CancellationToken cancellationToken);

    /// <exception cref="InvalidOperationException">No secret exists for that reference.</exception>
    Task<string> GetAsync(string credentialsRef, CancellationToken cancellationToken);

    /// <summary>Idempotent — deleting an unknown reference is not an error.</summary>
    Task DeleteAsync(string credentialsRef, CancellationToken cancellationToken);
}
