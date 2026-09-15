namespace Orbita.Application.Channels;

/// <summary>
/// A channel adapter's send call failed (ORB-B05). Internal to the send pipeline — a
/// dispatcher catches this to decide retry vs. permanent failure, it never reaches HTTP.
/// </summary>
public sealed class ChannelSendException(string errorCode, bool isTransient, string message) : Exception(message)
{
    /// <summary>The provider's own error code (e.g. Meta's numeric <c>error.code</c>, as a string).</summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>Whether retrying later might succeed — see <see cref="MetaErrorCatalog"/>.</summary>
    public bool IsTransient { get; } = isTransient;
}
