namespace Orbita.Application.Ai;

public sealed class DocumentTooLargeException(long sizeBytes, long maxBytes)
    : Exception($"The document is {sizeBytes / (1024 * 1024)} MB; the limit is {maxBytes / (1024 * 1024)} MB.")
{
    public long SizeBytes { get; } = sizeBytes;

    public long MaxBytes { get; } = maxBytes;
}
