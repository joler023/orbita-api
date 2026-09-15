using Orbita.Domain.Channels;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Channels;

public sealed record CreateTemplateRequest(Guid ChannelAccountId, string MetaTemplateName, MessageCategory Category, string? Language, string Body);

public sealed record MessageTemplateSummary(
    Guid Id,
    Guid ChannelAccountId,
    string MetaTemplateName,
    MessageCategory Category,
    string Language,
    string Body,
    TemplateStatus Status,
    string? RejectedReason,
    DateTimeOffset? ApprovedAt);
