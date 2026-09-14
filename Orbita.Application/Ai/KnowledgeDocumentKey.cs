namespace Orbita.Application.Ai;

/// <summary>
/// Storage keys for knowledge documents (ORB-C02), following the same
/// <c>tenants/{tenantId}/…</c> shape ORB-B06 established for conversation media.
///
/// The shared prefix is not cosmetic: it is what makes the tenant id part of the key
/// itself, so a key can be checked against the tenant that asked for it rather than
/// trusted. The two builders stay separate only because the segments after the prefix
/// differ; when ORB-B06 and this land on the same branch they are worth folding into one
/// type.
///
/// The original file name never appears in the key — it is user input, and it is already
/// preserved as the document's title.
/// </summary>
public static class KnowledgeDocumentKey
{
    public static string For(Guid tenantId, Guid documentId, string extension)
        => $"tenants/{tenantId:D}/knowledge/{documentId:D}{Normalize(extension)}";

    /// <summary>
    /// Rejects a key namespaced under a different tenant, and any attempt to climb out of
    /// the tenant's own prefix. Mirrors <c>MediaKeyBuilder.BelongsTo</c>.
    /// </summary>
    public static bool BelongsTo(string key, Guid tenantId)
        => !key.Contains("..", StringComparison.Ordinal)
            && key.StartsWith($"tenants/{tenantId:D}/", StringComparison.Ordinal);

    private static string Normalize(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim().ToLowerInvariant();

        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }
}
