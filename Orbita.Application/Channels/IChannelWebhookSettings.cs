namespace Orbita.Application.Channels;

/// <summary>
/// The two pieces of platform-wide webhook configuration the connection flow needs
/// (ORB-B01). A port rather than IConfiguration/IOptions so Application stays free of
/// configuration framework types — same reasoning as IRequestContext.
/// </summary>
public interface IChannelWebhookSettings
{
    /// <summary>
    /// Where Meta can reach this API from the public internet, without a trailing slash
    /// (e.g. "https://api.orbita.app"). Empty in local development, in which case
    /// per-account callback registration is skipped and Meta's app-level callback is
    /// the only route in.
    /// </summary>
    string PublicBaseUrl { get; }

    /// <summary>The hub.verify_token configured on Meta's app-level webhook callback.</summary>
    string GlobalVerifyToken { get; }
}
