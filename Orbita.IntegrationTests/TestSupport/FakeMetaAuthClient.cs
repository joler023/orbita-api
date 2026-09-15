using Orbita.Application.Channels;

namespace Orbita.IntegrationTests.TestSupport;

/// <summary>
/// Stands in for Graph API's OAuth endpoints (ORB-B01) — there is no Meta app to talk
/// to from the test suite. Any code exchanges into a deterministic token derived from
/// it, so a test can predict what the credential store ends up holding.
/// </summary>
public sealed class FakeMetaAuthClient : IMetaAuthClient
{
    public static string TokenFor(string code) => $"fake-access-token-{code}";

    public Task<MetaAccessToken> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
        => Task.FromResult(new MetaAccessToken(TokenFor(code), DateTimeOffset.UtcNow.AddDays(60)));

    public Task<DateTimeOffset?> GetTokenExpiryAsync(string accessToken, CancellationToken cancellationToken)
        => Task.FromResult<DateTimeOffset?>(DateTimeOffset.UtcNow.AddDays(60));
}
