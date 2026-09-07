namespace Orbita.Application.Ai;

/// <summary>
/// The single failure signal every <see cref="ILlmProvider"/> adapter raises. Adapters
/// translate their own transport errors into this so <see cref="ResilientLlmProvider"/>
/// can make one decision without knowing anything about HTTP or about which provider
/// answered.
/// </summary>
public sealed class LlmProviderException : Exception
{
    public LlmProviderException(string providerName, string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderName = providerName;
        IsTransient = isTransient;
    }

    /// <summary>Which <see cref="ILlmProvider.Name"/> failed.</summary>
    public string ProviderName { get; }

    /// <summary>
    /// True when the provider itself is the problem — it timed out, refused the
    /// connection, or answered 5xx/429. Those are worth retrying, and worth failing
    /// over to another provider.
    ///
    /// False when the *request* is the problem — a 4xx: an unknown model, a malformed
    /// tool schema, a context window overflow. Retrying that gets the same answer, and
    /// so does asking a different provider, so <see cref="ResilientLlmProvider"/>
    /// surfaces it immediately instead of burning the fallback chain on it.
    /// </summary>
    public bool IsTransient { get; }
}
