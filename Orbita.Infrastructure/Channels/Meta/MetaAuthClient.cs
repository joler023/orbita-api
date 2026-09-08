using Microsoft.Extensions.Configuration;
using Orbita.Application.Channels;

namespace Orbita.Infrastructure.Channels.Meta;

/// <summary>
/// Graph API OAuth calls (ORB-B01). Needs <c>Channels:Meta:AppId</c> and
/// <c>Channels:Meta:AppSecret</c> configured — both empty placeholders in appsettings
/// until a real Meta app exists, the same convention as Billing:Stripe. Registered
/// with HttpClientFactory's default request logging removed (see DependencyInjection):
/// the app secret and every access token travel in these URLs, and "el token nunca
/// en logs" is a rule, not a preference.
/// </summary>
public sealed class MetaAuthClient : IMetaAuthClient
{
    public const string DefaultGraphApiBaseUrl = "https://graph.facebook.com/v21.0/";

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly string _appId;
    private readonly string _appSecret;

    public MetaAuthClient(HttpClient httpClient, IConfiguration configuration, TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(configuration["Channels:Meta:GraphApiBaseUrl"] ?? DefaultGraphApiBaseUrl);
        _timeProvider = timeProvider;
        _appId = configuration["Channels:Meta:AppId"] ?? string.Empty;
        _appSecret = configuration["Channels:Meta:AppSecret"] ?? string.Empty;
    }

    public async Task<MetaAccessToken> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var url = $"oauth/access_token?client_id={Uri.EscapeDataString(_appId)}"
            + $"&client_secret={Uri.EscapeDataString(_appSecret)}"
            + $"&code={Uri.EscapeDataString(code)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var token = await MetaGraphResponseReader.ReadAsync<MetaTokenResponse>(response, cancellationToken);

        var expiresAt = token.ExpiresInSeconds is { } seconds and > 0
            ? _timeProvider.GetUtcNow().AddSeconds(seconds)
            : (DateTimeOffset?)null;
        return new MetaAccessToken(token.AccessToken, expiresAt);
    }

    public async Task<DateTimeOffset?> GetTokenExpiryAsync(string accessToken, CancellationToken cancellationToken)
    {
        var appToken = $"{_appId}|{_appSecret}";
        var url = $"debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={Uri.EscapeDataString(appToken)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var debug = await MetaGraphResponseReader.ReadAsync<MetaDebugTokenResponse>(response, cancellationToken);

        if (!debug.Data.IsValid)
        {
            throw new MetaApiException("The access token is not valid.");
        }

        return debug.Data.ExpiresAtUnixSeconds is { } unix and > 0
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;
    }
}
