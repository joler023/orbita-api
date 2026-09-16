using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-C11: the test exchanges an owner saved to run again after changing their assistant.
///
/// There is no "run" endpoint: the screen posts a saved case's messages to
/// <c>POST .../test-chat</c>, which is the same path a fresh exchange takes.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/test-cases")]
public sealed class AgentTestCasesController(IAgentTestCaseService testCases) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AgentTestCaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AgentTestCaseDto>>> List(
        Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => Ok(await testCases.ListAsync(tenantId, User.GetUserId(), agentId, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(AgentTestCaseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentTestCaseDto>> Save(
        Guid tenantId,
        Guid agentId,
        [FromBody] SaveTestCaseBody body,
        CancellationToken cancellationToken)
    {
        var saved = await testCases.SaveAsync(tenantId, User.GetUserId(), agentId, body.Name, body.Messages, cancellationToken);

        return CreatedAtAction(nameof(List), new { tenantId, agentId }, saved);
    }

    [HttpDelete("{testCaseId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(
        Guid tenantId, Guid agentId, Guid testCaseId, CancellationToken cancellationToken)
    {
        await testCases.DeleteAsync(tenantId, User.GetUserId(), agentId, testCaseId, cancellationToken);

        return NoContent();
    }
}

/// <param name="Name">The owner's own words. Not derived from the first message.</param>
/// <param name="Messages">The exchange, oldest first, in the same shape <c>test-chat</c> takes.</param>
public sealed record SaveTestCaseBody(
    [Required, MaxLength(AgentTestCase.NameMaxLength)] string Name,
    [Required, MaxLength(AgentTestCase.MaxTurns)] IReadOnlyList<AgentTestTurn> Messages);
