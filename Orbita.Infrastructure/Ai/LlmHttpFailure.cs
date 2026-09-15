using System.Net;
using Orbita.Application.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Turns transport-level failures into the single signal
/// <see cref="ResilientLlmProvider"/> understands. Shared by both adapters so the
/// "is this worth retrying?" rule is written once — getting it wrong in one adapter
/// would silently disable failover for that provider.
/// </summary>
internal static class LlmHttpFailure
{
    /// <summary>
    /// A response came back, but not a successful one.
    ///
    /// 5xx and 429 mean the provider is struggling: retry, then fail over. Every other
    /// 4xx means the request is wrong (unknown model, malformed tool schema, context
    /// too long) — no amount of retrying or switching providers fixes that.
    /// </summary>
    public static LlmProviderException FromStatus(string providerName, HttpStatusCode status, string body)
    {
        var isTransient = (int)status >= 500 || status == HttpStatusCode.TooManyRequests;

        return new LlmProviderException(
            providerName,
            $"{providerName} responded {(int)status} {status}: {Truncate(body)}",
            isTransient);
    }

    /// <summary>
    /// The request never got an answer — connection refused (the local model server
    /// isn't running), DNS failure, or a timeout. Always transient: this is exactly the
    /// "un proveedor se cae" case ORB-C01 asks failover to cover.
    /// </summary>
    public static LlmProviderException FromTransport(string providerName, Exception exception)
        => new(providerName, $"{providerName} is unreachable: {exception.Message}", isTransient: true, exception);

    /// <summary>The provider answered 200 with something that isn't the shape its own API documents.</summary>
    public static LlmProviderException FromMalformedResponse(string providerName, string detail)
        => new(providerName, $"{providerName} returned an unusable response: {detail}", isTransient: false);

    private static string Truncate(string body)
        => body.Length <= 500 ? body : string.Concat(body.AsSpan(0, 500), "…");
}
