namespace Orbita.Application.Channels;

/// <summary>
/// Periodic sweep that flips connected accounts whose access token has passed its
/// expiry to <c>TokenExpired</c> (ORB-B01: "avisa antes de que el token caduque" —
/// the "soon" warning is computed per read in ChannelAccountDto; this handles the
/// moment it actually happens). Driven by a background worker, not by requests.
/// </summary>
public interface IChannelTokenExpiryService
{
    /// <returns>How many accounts were marked expired in this sweep.</returns>
    Task<int> MarkExpiredAsync(CancellationToken cancellationToken);
}
