namespace Orbita.Application.Media;

/// <summary>The media key given to SendMediaAsync doesn't belong to this tenant — never a real object, whether it exists elsewhere or not.</summary>
public sealed class MediaKeyNotFoundException() : Exception("Media not found.");
