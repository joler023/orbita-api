using Orbita.Domain.Channels;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Channels;

/// <summary>One thing a channel's webhook payload can carry once parsed (ORB-B03): a message, or a delivery status update.</summary>
public abstract record InboundItem;

public sealed record InboundMessage(
    string AccountExternalId,
    string ExternalId,
    string SenderExternalId,
    string? SenderDisplayName,
    ChannelKind Kind,
    string? Body,
    string? MediaExternalId,
    string? MediaMime,
    string? ReplyToExternalId,
    DateTimeOffset Timestamp) : InboundItem;

/// <summary>Delivered/read/failed receipts for a message this tenant sent. Parsed starting ORB-B03; acted on starting ORB-B08.</summary>
public sealed record InboundStatusUpdate(
    string AccountExternalId,
    string ExternalId,
    ChannelKind Kind,
    MessageStatus Status,
    string? ErrorCode,
    DateTimeOffset Timestamp) : InboundItem;
