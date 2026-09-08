namespace Orbita.Application.Channels;

/// <summary>
/// Meta's OAuth surface (ORB-B01): turns the short-lived <c>code</c> the dashboard
/// obtains from Embedded Signup into an access token we can keep, and reads that
/// token's expiry. Split from <see cref="IWhatsAppCloudApiClient"/> because Instagram
/// (ORB-B09) shares this part and nothing else.
/// </summary>
public interface IMetaAuthClient
{
    /// <exception cref="MetaApiException">Meta rejected the code (expired, already used, wrong app).</exception>
    Task<MetaAccessToken> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Null when the token never expires (system-user tokens) or Meta does not report an expiry.</summary>
    /// <exception cref="MetaApiException">The token is invalid.</exception>
    Task<DateTimeOffset?> GetTokenExpiryAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed record MetaAccessToken(string AccessToken, DateTimeOffset? ExpiresAt);
