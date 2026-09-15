using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Channels;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-B01: a tenant's connected messaging channels. The tenant comes from the route,
/// never from the JWT (see CLAUDE.md's Team invitations section for why).
/// </summary>
[ApiController]
[Authorize]
public sealed class ChannelsController(IWhatsAppChannelService whatsAppChannelService) : ControllerBase
{
    /// <summary>
    /// Finishes Meta's Embedded Signup: the dashboard sends the one-time OAuth code and
    /// the ids the signup dialog reported, the backend exchanges and stores the token.
    /// The account starts as PendingVerification and becomes Connected once Meta has
    /// verified the webhook callback (a separate, anonymous GET from Meta).
    /// </summary>
    [HttpPost("api/tenants/{tenantId:guid}/channels/whatsapp")]
    [ProducesResponseType(typeof(ChannelAccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ChannelAccountDto>> ConnectWhatsApp(
        Guid tenantId,
        [FromBody] ConnectWhatsAppRequest request,
        CancellationToken cancellationToken)
    {
        var account = await whatsAppChannelService.ConnectAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, account);
    }

    [HttpGet("api/tenants/{tenantId:guid}/channels")]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ChannelAccountDto>>> List(Guid tenantId, CancellationToken cancellationToken)
    {
        var accounts = await whatsAppChannelService.ListAsync(tenantId, User.GetUserId(), cancellationToken);
        return Ok(accounts);
    }

    [HttpGet("api/tenants/{tenantId:guid}/channels/{channelAccountId:guid}")]
    [ProducesResponseType(typeof(ChannelAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelAccountDto>> Get(Guid tenantId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        var account = await whatsAppChannelService.GetAsync(tenantId, User.GetUserId(), channelAccountId, cancellationToken);
        return Ok(account);
    }

    /// <summary>Retries the webhook subscription for an account still pending verification.</summary>
    [HttpPost("api/tenants/{tenantId:guid}/channels/{channelAccountId:guid}/verify")]
    [ProducesResponseType(typeof(ChannelAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ChannelAccountDto>> Verify(Guid tenantId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        var account = await whatsAppChannelService.VerifyAsync(tenantId, User.GetUserId(), channelAccountId, cancellationToken);
        return Ok(account);
    }

    /// <summary>Disconnects the account and deletes its stored credential. Conversation history is kept.</summary>
    [HttpDelete("api/tenants/{tenantId:guid}/channels/{channelAccountId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Disconnect(Guid tenantId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        await whatsAppChannelService.DisconnectAsync(tenantId, User.GetUserId(), channelAccountId, cancellationToken);
        return NoContent();
    }
}
