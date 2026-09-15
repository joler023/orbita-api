namespace Orbita.Infrastructure.Channels;

/// <summary>
/// One encrypted channel access token, the stand-in for a Secrets Manager secret
/// (ORB-B01). Infrastructure-only on purpose — the domain never sees this type, only
/// the opaque reference <see cref="DataProtectionChannelCredentialStore"/> derives
/// from <see cref="Id"/>. Not tenant-scoped: it is addressed by reference alone, like
/// the secret it stands in for.
/// </summary>
public sealed class ChannelCredential
{
    private ChannelCredential()
    {
        Ciphertext = string.Empty;
    }

    public ChannelCredential(Guid id, string ciphertext, DateTimeOffset createdAt)
    {
        Id = id;
        Ciphertext = ciphertext;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Ciphertext { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
