namespace Orbita.Infrastructure.Media;

/// <summary>Where LocalFileMediaStorage keeps files — a local stand-in for an R2/S3 bucket path (ORB-B06).</summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public string LocalStoragePath { get; set; } = "media-storage";
}
