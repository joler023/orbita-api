using Microsoft.Extensions.Configuration;
using Orbita.Application.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Keeps uploaded documents on local disk.
///
/// This is the stand-in for Cloudflare R2, which orbita-schema.dbml expects
/// (<c>source_ref</c> is meant to hold an R2 object key) but which is not provisioned
/// yet — the same documented placeholder pattern as logging invitation emails instead of
/// sending them, and Data Protection instead of KMS. It is fine for one developer
/// machine and wrong for a multi-instance deployment, where two API nodes would not see
/// each other's files.
///
/// Keys are <c>{tenantId}/{guid}{extension}</c> and every read is re-rooted under the
/// configured directory, so a crafted key cannot escape it — the same containment R2's
/// per-tenant prefix gives.
/// </summary>
public sealed class LocalDiskKnowledgeDocumentStorage : IKnowledgeDocumentStorage
{
    private readonly string _rootPath;

    public LocalDiskKnowledgeDocumentStorage(IConfiguration configuration)
    {
        var configured = configuration["Ai:KnowledgeStorage:RootPath"];

        _rootPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "orbita", "knowledge")
            : configured;

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Guid tenantId, string fileName, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The original name is never part of the key: it is user input, and it is already
        // preserved as the document's title.
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var key = $"{tenantId}/{Guid.NewGuid()}{extension}";
        var path = ResolvePath(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);

        return key;
    }

    public Task<Stream> OpenAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(storageKey);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The stored document is missing.", storageKey);
        }

        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(storageKey);

        // Already gone is the desired end state, not an error.
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a key under the root and refuses anything that climbs out of it — a key
    /// is data that reached us through an API, so it is never trusted as a path.
    /// </summary>
    private string ResolvePath(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var root = Path.GetFullPath(_rootPath);
        var resolved = Path.GetFullPath(Path.Combine(root, storageKey));

        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Storage key '{storageKey}' resolves outside the document store.");
        }

        return resolved;
    }
}
