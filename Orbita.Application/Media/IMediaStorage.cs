namespace Orbita.Application.Media;

/// <summary>Where media bytes actually live (ORB-B06) — a local filesystem stand-in for R2/S3.</summary>
public interface IMediaStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <returns>Null if no object exists for that key.</returns>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
}
