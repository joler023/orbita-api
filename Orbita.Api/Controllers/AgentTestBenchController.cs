using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-C11: screen 2.8's test bench — an owner talking to their own assistant before any
/// customer does.
///
/// Separate from <see cref="AiAgentsController"/> for the same reason ORB-C03's search is
/// separate from document administration: configuring an assistant and exercising one
/// change for different reasons.
/// </summary>
[ApiController]
[Authorize]
public sealed class AgentTestBenchController(IAgentTestBenchService testBench) : ControllerBase
{
    /// <summary>
    /// Answers as the assistant would, against its unpublished changes when it has any.
    /// Nothing is persisted except the <c>ai_runs</c> measurement — the transcript lives in
    /// the caller's hands.
    /// </summary>
    [HttpPost("api/tenants/{tenantId:guid}/ai-agents/{agentId:guid}/test-chat")]
    [ProducesResponseType(typeof(AgentTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<AgentTestResult>> Run(
        Guid tenantId,
        Guid agentId,
        [FromBody] TestChatBody body,
        CancellationToken cancellationToken)
        => Ok(await testBench.RunAsync(tenantId, User.GetUserId(), agentId, body.ToRequest(), cancellationToken));
}

/// <param name="History">
/// The exchange so far, oldest first. Omitting it starts a fresh conversation — only the
/// last <see cref="AgentTestBenchService.MaxHistoryTurns"/> turns reach the model.
/// </param>
public sealed record TestChatBody(
    [Required, MaxLength(AgentTestBenchService.MaxMessageLength)] string Message,
    IReadOnlyList<AgentTestTurn>? History = null)
{
    public AgentTestRequest ToRequest() => new(Message, History ?? []);
}
