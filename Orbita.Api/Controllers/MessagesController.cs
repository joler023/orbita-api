using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Inbox;

namespace Orbita.Api.Controllers;

/// <summary>ORB-B05: sending outbound messages on a conversation.</summary>
[ApiController]
[Authorize]
public sealed class MessagesController(IOutboundMessageService outboundMessageService) : ControllerBase
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
}
