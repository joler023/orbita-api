using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Orbita.Application.Billing;
using Orbita.Infrastructure.Channels;

namespace Orbita.IntegrationTests;

/// <summary>
/// No database involved — lives here rather than Orbita.UnitTests only because
/// Orbita.UnitTests doesn't reference Orbita.Infrastructure.
/// </summary>
public sealed class MetaWebhookSignatureVerifierTests
{
    private const string AppSecret = "test-app-secret";

    private readonly MetaWebhookSignatureVerifier _sut = new(BuildConfiguration(AppSecret));

    [Fact]
    public void Verify_WithAMatchingSignature_DoesNotThrow()
    {
        var body = "{\"hello\":\"world\"}"u8.ToArray();
        var signature = Sign(AppSecret, body);

        var exception = Record.Exception(() => _sut.Verify(body, signature));

        Assert.Null(exception);
    }

    [Fact]
    public void Verify_WithAWrongSecret_Throws()
    {
        var body = "{\"hello\":\"world\"}"u8.ToArray();
        var signature = Sign("a-different-secret", body);

        Assert.Throws<InvalidWebhookSignatureException>(() => _sut.Verify(body, signature));
    }

    [Fact]
    public void Verify_WithATamperedBody_Throws()
    {
        var signature = Sign(AppSecret, "{\"hello\":\"world\"}"u8.ToArray());

        Assert.Throws<InvalidWebhookSignatureException>(() => _sut.Verify("{\"hello\":\"mallory\"}"u8.ToArray(), signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-prefixed-with-sha256")]
    [InlineData("sha256=not-hex")]
    public void Verify_WithAMissingOrMalformedHeader_Throws(string? signatureHeader)
        => Assert.Throws<InvalidWebhookSignatureException>(() => _sut.Verify("{}"u8.ToArray(), signatureHeader));

    [Fact]
    public void Verify_WithNoConfiguredAppSecret_AlwaysThrows()
    {
        var sut = new MetaWebhookSignatureVerifier(BuildConfiguration(string.Empty));
        var body = "{}"u8.ToArray();

        Assert.Throws<InvalidWebhookSignatureException>(() => sut.Verify(body, Sign(AppSecret, body)));
    }

    private static string Sign(string secret, byte[] body)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    private static IConfiguration BuildConfiguration(string appSecret)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Channels:Meta:AppSecret"] = appSecret })
            .Build();
}
