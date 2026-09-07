using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Identity;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A11: two-factor setup, confirmation, and management for the caller's own account.</summary>
[ApiController]
[Route("api/auth/2fa")]
[Authorize]
public sealed class TwoFactorController(ITwoFactorService twoFactorService) : ControllerBase
{
    [HttpPost("setup")]
    [ProducesResponseType(typeof(TwoFactorSetupResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<TwoFactorSetupResult>> Setup(CancellationToken cancellationToken)
    {
        var result = await twoFactorService.BeginSetupAsync(User.GetUserId(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("confirm")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<string>>> Confirm([FromBody] ConfirmTwoFactorSetupRequest request, CancellationToken cancellationToken)
    {
        var backupCodes = await twoFactorService.ConfirmSetupAsync(User.GetUserId(), request.Code, cancellationToken);
        return Ok(backupCodes);
    }

    [HttpPost("disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Disable([FromBody] DisableTwoFactorRequest request, CancellationToken cancellationToken)
    {
        await twoFactorService.DisableAsync(User.GetUserId(), request.CurrentPassword, cancellationToken);
        return NoContent();
    }

    [HttpPost("backup-codes/regenerate")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<string>>> RegenerateBackupCodes(CancellationToken cancellationToken)
    {
        var backupCodes = await twoFactorService.RegenerateBackupCodesAsync(User.GetUserId(), cancellationToken);
        return Ok(backupCodes);
    }
}
