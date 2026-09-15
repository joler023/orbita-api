using Orbita.Domain.Inbox;

namespace Orbita.Application.Media;

/// <summary>Builds and validates media storage keys (ORB-B06) — the tenant id prefix is what makes <see cref="BelongsTo"/> a real isolation check, not decoration.</summary>
public static class MediaKeyBuilder
{
    public static string ForMessage(Guid tenantId, Guid conversationId, Guid mediaId, string mime)
        => $"tenants/{tenantId:D}/conversations/{conversationId:D}/{mediaId:D}.{MediaMimeCatalog.ExtensionFor(mime)}";

    /// <summary>Rejects a key namespaced under a different tenant and any attempt to traverse out of the tenant's own prefix.</summary>
    public static bool BelongsTo(string key, Guid tenantId)
        => !key.Contains("..", StringComparison.Ordinal) && key.StartsWith($"tenants/{tenantId:D}/", StringComparison.Ordinal);
}
