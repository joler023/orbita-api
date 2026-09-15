namespace Orbita.Application.Media;

public interface IMediaService
{
    /// <exception cref="UnsupportedMediaTypeException"/>
    /// <exception cref="MediaTooLargeException"/>
    Task<UploadUrlDto> CreateUploadUrlAsync(Guid tenantId, Guid callerUserId, Guid conversationId, CreateUploadUrlRequest request, CancellationToken cancellationToken);

    Task<MediaUrlDto> GetMessageMediaUrlAsync(Guid tenantId, Guid callerUserId, Guid messageId, CancellationToken cancellationToken);
}
