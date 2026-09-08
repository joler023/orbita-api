namespace Orbita.Application.Inbox;

public sealed record SendMediaRequest(string MediaKey, string MediaMime, string? Caption);
