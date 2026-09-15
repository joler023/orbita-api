namespace Orbita.Domain.Channels;

/// <summary>Mirrors orbita-schema.dbml's channel_status enum (ORB-B01).</summary>
public enum ChannelStatus
{
    /// <summary>Credentials stored, but Meta has not yet verified our webhook callback for this account.</summary>
    PendingVerification,

    Connected,

    /// <summary>The stored access token passed its expiry — messages can no longer be sent until the admin reconnects.</summary>
    TokenExpired,

    /// <summary>Disconnected by an admin. Conversation history is kept; only the credential is gone.</summary>
    Disconnected,
}
