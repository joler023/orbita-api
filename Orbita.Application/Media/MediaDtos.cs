namespace Orbita.Application.Media;

public sealed record CreateUploadUrlRequest(string Mime, string FileName, long SizeBytes);

public sealed record UploadUrlDto(string Key, string UploadUrl, DateTimeOffset ExpiresAt);

public sealed record MediaUrlDto(string Url, DateTimeOffset ExpiresAt);
