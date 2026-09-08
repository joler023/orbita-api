using Orbita.Application.Channels;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

/// <param name="MediaUrl">A 15-minute signed URL when the message has media (ORB-B06); null otherwise.</param>
/// <param name="ErrorMessage">Human-readable (Spanish) translation of <paramref name="ErrorCode"/>, computed at read time — see MetaErrorCatalog.</param>
public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    MessageDirection Direction,
    MessageCategory Category,
    string? Body,
    string? MediaUrl,
    MessageStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? SentByUserId,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt)
{
    public static MessageDto From(Message message, string? mediaUrl = null)
        => new(
            message.Id,
            message.ConversationId,
            message.Direction,
            message.Category,
            message.Body,
            mediaUrl,
            message.Status,
            message.ErrorCode,
            message.ErrorCode is null ? null : MetaErrorCatalog.Describe(message.ErrorCode).MessageEs,
            message.SentByUserId,
            message.SentAt,
            message.DeliveredAt,
            message.ReadAt,
            message.CreatedAt);
}
