using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

/// <param name="ExpiresSoon">True when the token expires within ChannelAccount.ExpiryWarningWindow — the dashboard's "reconnect soon" banner.</param>
public sealed record ChannelAccountDto(
    Guid Id,
    ChannelKind Kind,
    string ExternalId,
    string DisplayName,
    string? PhoneE164,
    ChannelStatus Status,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? TokenExpiresAt,
    bool ExpiresSoon)
{
    public static ChannelAccountDto From(ChannelAccount account, DateTimeOffset now)
        => new(
            account.Id,
            account.Kind,
            account.ExternalId,
            account.DisplayName,
            account.PhoneE164,
            account.Status,
            account.ConnectedAt,
            account.TokenExpiresAt,
            account.TokenExpiresSoon(now));
}
