using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Inbox;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-C07: the conversations an assistant handed to a person, and giving one back.
///
/// Its own controller rather than more routes on <see cref="MessagesController"/>: that
/// one is about sending, this is about who owns the thread. It is also the first read of
/// the inbox in this API — ORB-B12's listing, with its filters and search, is a different
/// story and will have its own.
/// </summary>
[ApiController]
[Authorize]
public sealed class HandoffsController(IConversationHandoffService handoffs) : ControllerBase
{
    /// <summary>
    /// The queue, oldest wait first. Paged like every list in this API
    /// (<c>{ items, nextCursor }</c>), plus the total: a queue that silently shows its
    /// first page as if it were all of it hides exactly the customer nobody got to.
    /// </summary>
    [HttpGet("api/tenants/{tenantId:guid}/handoffs")]
    [ProducesResponseType(typeof(HandoffQueuePage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HandoffQueuePage>> List(
        Guid tenantId,
        CancellationToken cancellationToken,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = ConversationHandoffService.DefaultPageSize)
        => Ok(await handoffs.ListWaitingAsync(tenantId, User.GetUserId(), cursor, limit, cancellationToken));

    /// <summary>
    /// Gives the conversation back to the assistant — ORB-C07's "salvo que un humano lo
    /// reactive". Answers 200 even when it was not handed over, because that is already
    /// the state being asked for.
    /// </summary>
    [HttpPost("api/tenants/{tenantId:guid}/conversations/{conversationId:guid}/return-to-assistant")]
    [ProducesResponseType(typeof(HandoffStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HandoffStateDto>> ReturnToAssistant(
        Guid tenantId,
        Guid conversationId,
        CancellationToken cancellationToken)
        => Ok(await handoffs.ReturnToAssistantAsync(tenantId, User.GetUserId(), conversationId, cancellationToken));
}
