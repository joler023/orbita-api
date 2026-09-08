namespace Orbita.Application.Channels;

/// <summary>
/// A Graph API call answered with an error object. Raised by Infrastructure clients,
/// caught and translated by application services — it never reaches HTTP as-is.
/// </summary>
public sealed class MetaApiException(string message, int? errorCode = null) : Exception(message)
{
    /// <summary>Meta's numeric <c>error.code</c>, when the response carried one.</summary>
    public int? ErrorCode { get; } = errorCode;
}
