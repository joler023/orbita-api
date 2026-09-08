using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Orbita.Application.Billing;
using Orbita.Application.Channels;

namespace Orbita.Infrastructure.Channels;

/// <summary>
/// Verifies Meta's <c>X-Hub-Signature-256</c> header: HMAC-SHA256 of the raw body,
/// keyed with the platform's Meta app secret (<c>Channels:Meta:AppSecret</c>), hex
/// encoded and prefixed <c>sha256=</c>. The app secret is platform-wide configuration,
/// not per-account state — unlike <see cref="Domain.Channels.ChannelAccount.WebhookSecret"/>,
/// which is the unrelated per-account hub.verify_token used only for the GET handshake.
/// </summary>
public sealed class MetaWebhookSignatureVerifier(IConfiguration configuration) : IWebhookSignatureVerifier
{
    private const string SignaturePrefix = "sha256=";

    public void Verify(byte[] rawBody, string? signatureHeader)
    {
        var appSecret = configuration["Channels:Meta:AppSecret"];
        if (string.IsNullOrEmpty(appSecret)
            || signatureHeader is null
            || !signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            throw new InvalidWebhookSignatureException();
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), rawBody);
        if (!TryDecodeHex(signatureHeader.AsSpan(SignaturePrefix.Length), out var provided)
            || !CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            throw new InvalidWebhookSignatureException();
        }
    }

    private static bool TryDecodeHex(ReadOnlySpan<char> hex, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
