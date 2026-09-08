using Microsoft.Extensions.Options;
using Orbita.Application.Media;

namespace Orbita.Infrastructure.Media;

/// <summary>
/// Local filesystem stand-in for R2/S3 (ORB-B06). <see cref="ResolvePath"/> is the
/// traversal guard: every key is resolved under <see cref="MediaOptions.LocalStoragePath"/>
/// and the result must still start with that root — a key containing <c>..</c> (or an
/// absolute path) that would otherwise escape it is rejected instead of silently
/// reading/writing outside the media root.
/// </summary>
public sealed class LocalFileMediaStorage(IOptions<MediaOptions> options) : IMediaStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.LocalStoragePath);

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    private string ResolvePath(string key)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, key));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Media key resolves outside the storage root.");
        }

        return fullPath;
    }
}
