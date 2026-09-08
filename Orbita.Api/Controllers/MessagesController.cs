using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Inbox;
using Orbita.Application.Media;

namespace Orbita.Api.Controllers;

/// <summary>ORB-B05/ORB-B06: sending outbound messages (text and media) on a conversation.</summary>
[ApiController]
[Authorize]
public sealed class MessagesController(IOutboundMessageService outboundMessageService, IMediaService mediaService) : ControllerBase
{
    [HttpPost("api/tenants/{tenantId:guid}/conversations/{conversationId:guid}/messages")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MessageDto>> SendText(
        Guid tenantId,
        Guid conversationId,
        [FromBody] SendTextRequest request,
        CancellationToken cancellationToken)
    {
        var message = await outboundMessageService.SendTextAsync(tenantId, User.GetUserId(), conversationId, request, cancellationToken);
        return Accepted(message);
    }

    [HttpPost("api/tenants/{tenantId:guid}/conversations/{conversationId:guid}/media/upload-url")]
    [ProducesResponseType(typeof(UploadUrlDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<UploadUrlDto>> CreateUploadUrl(
        Guid tenantId,
        Guid conversationId,
        [FromBody] CreateUploadUrlRequest request,
        CancellationToken cancellationToken)
    {
        var uploadUrl = await mediaService.CreateUploadUrlAsync(tenantId, User.GetUserId(), conversationId, request, cancellationToken);
        return Created(uploadUrl.UploadUrl, uploadUrl);
    }

    [HttpPost("api/tenants/{tenantId:guid}/conversations/{conversationId:guid}/messages/media")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MessageDto>> SendMedia(
        Guid tenantId,
        Guid conversationId,
        [FromBody] SendMediaRequest request,
        CancellationToken cancellationToken)
    {
        var message = await outboundMessageService.SendMediaAsync(tenantId, User.GetUserId(), conversationId, request, cancellationToken);
        return Accepted(message);
    }

    [HttpGet("api/tenants/{tenantId:guid}/messages/{messageId:guid}/media-url")]
    [ProducesResponseType(typeof(MediaUrlDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaUrlDto>> GetMediaUrl(Guid tenantId, Guid messageId, CancellationToken cancellationToken)
    {
        return Ok(await mediaService.GetMessageMediaUrlAsync(tenantId, User.GetUserId(), messageId, cancellationToken));
    }

    [HttpPost("api/tenants/{tenantId:guid}/conversations/{conversationId:guid}/messages/template")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MessageDto>> SendTemplate(
        Guid tenantId,
        Guid conversationId,
        [FromBody] SendTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var message = await outboundMessageService.SendTemplateAsync(tenantId, User.GetUserId(), conversationId, request, cancellationToken);
        return Accepted(message);
    }
}
