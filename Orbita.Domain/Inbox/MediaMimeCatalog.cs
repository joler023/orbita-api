namespace Orbita.Domain.Inbox;

/// <summary>Which media types this app accepts and how big each may be (ORB-B06) — mirrors WhatsApp Cloud API's own limits.</summary>
public static class MediaMimeCatalog
{
    private static readonly IReadOnlyDictionary<string, (string Extension, long MaxSizeBytes)> AllowedTypes
        = new Dictionary<string, (string, long)>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ("jpg", 5 * 1024 * 1024),
            ["image/png"] = ("png", 5 * 1024 * 1024),
            ["image/webp"] = ("webp", 5 * 1024 * 1024),
            ["audio/mpeg"] = ("mp3", 16 * 1024 * 1024),
            ["audio/ogg"] = ("ogg", 16 * 1024 * 1024),
            ["video/mp4"] = ("mp4", 16 * 1024 * 1024),
            ["application/pdf"] = ("pdf", 100 * 1024 * 1024),
            ["application/msword"] = ("doc", 100 * 1024 * 1024),
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ("docx", 100 * 1024 * 1024),
        };

    public static bool IsAllowed(string mime) => AllowedTypes.ContainsKey(mime);

    public static long MaxSizeBytesFor(string mime)
        => AllowedTypes.TryGetValue(mime, out var entry) ? entry.MaxSizeBytes : 0;

    public static string ExtensionFor(string mime)
        => AllowedTypes.TryGetValue(mime, out var entry) ? entry.Extension : "bin";
}
