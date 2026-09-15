using Orbita.Application.Channels;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

/// <param name="MediaUrl">A 15-minute signed URL when the message has media (ORB-B06); null otherwise.</param>
/// <param name="ErrorMessage">Human-readable (Spanish) translation of <paramref name="ErrorCode"/>, computed at read time — see MetaErrorCatalog.</param>
/// <param name="AuthorKind">
/// Who wrote it. Read this instead of inferring the author from
/// <paramref name="SentByUserId"/> being null: that is only equivalent while the
/// assistant is the single non-human sender in the product.
/// </param>
/// <param name="AiRunId">
/// The <c>ai_runs</c> row this reply came from (ORB-C04), so a screen can show what the
/// assistant retrieved and what the answer cost. Null for anything a person sent.
/// </param>
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
    MessageAuthorKind AuthorKind,
    Guid? AiRunId,
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
            message.AuthorKind,
            message.AiRunId,
            message.SentAt,
            message.DeliveredAt,
            message.ReadAt,
            message.CreatedAt);
}
