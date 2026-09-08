namespace Orbita.Application.Channels;

/// <summary>
/// What the dashboard has at the end of Meta's Embedded Signup flow (ORB-B01): the
/// one-time OAuth <paramref name="Code"/> plus the ids the signup dialog reports back.
/// </summary>
public sealed record ConnectWhatsAppRequest(string Code, string WabaId, string PhoneNumberId);
