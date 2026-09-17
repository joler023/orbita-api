using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-C13: which model serves each task, per tenant. Requires
/// <c>Permission.ManageAiModels</c> — <b>Owner only</b>, not the Owner-and-Admin
/// <c>ManageAiAgents</c> it used to take: this changes what customers are answered with
/// <em>and what the organization is billed</em>, and money is where this product already
/// draws the Owner-only line (<c>ManageBilling</c>).
///
/// The provider is part of the route because model ids are not portable: the same task
/// resolves to a different id on a hosted gateway than on a local Ollama, so a preference
/// that did not name one would be ambiguous. <c>GET /api/ai-providers</c> is how a screen
/// learns which names exist.
/// </summary>
[ApiController]
[Authorize]
public sealed class ModelPreferencesController(
    IModelPreferenceService preferences,
    ILlmProviderCatalog providerCatalog) : ControllerBase
{
    /// <summary>
    /// Which values <c>{providerName}</c> can take. Not tenant-scoped and not authorized
    /// beyond being signed in, like <c>GET /api/ai-tools</c>: it describes the deployment,
    /// not an organization, and knowing a provider's name grants nothing — changing a
    /// preference still requires <c>ManageAiModels</c>.
    /// </summary>
    [HttpGet("api/ai-providers")]
    [ProducesResponseType(typeof(IReadOnlyList<LlmProviderDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<LlmProviderDto>> ListProviders() => Ok(providerCatalog.List());

    [HttpGet("api/tenants/{tenantId:guid}/ai-models/{providerName}")]
    [ProducesResponseType(typeof(IReadOnlyList<ModelPreferenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ModelPreferenceDto>>> List(
        Guid tenantId,
        string providerName,
        CancellationToken cancellationToken)
        => Ok(await preferences.ListAsync(tenantId, User.GetUserId(), providerName, cancellationToken));

    [HttpPut("api/tenants/{tenantId:guid}/ai-models/{providerName}/{task}")]
    [ProducesResponseType(typeof(ModelPreferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ModelPreferenceDto>> Set(
        Guid tenantId,
        string providerName,
        LlmTask task,
        [FromBody] SetModelPreferenceRequest request,
        CancellationToken cancellationToken)
        => Ok(await preferences.SetAsync(
            tenantId, User.GetUserId(), providerName, task, request.Model, cancellationToken));

    /// <summary>Falls back to the deployment default. Clearing something already absent is not an error.</summary>
    [HttpDelete("api/tenants/{tenantId:guid}/ai-models/{providerName}/{task}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Clear(
        Guid tenantId,
        string providerName,
        LlmTask task,
        CancellationToken cancellationToken)
    {
        await preferences.ClearAsync(tenantId, User.GetUserId(), providerName, task, cancellationToken);
        return NoContent();
    }
}

/// <param name="Model">
/// The provider's own model id. Deliberately not validated against a catalog: the whole
/// point of ORB-C01's abstraction is that a gateway can add models without this API
/// knowing, and a stale allow-list would block exactly the switch this feature exists for.
/// A wrong id surfaces as a 4xx from the provider on the next call.
/// </param>
public sealed record SetModelPreferenceRequest(
    [Required, MaxLength(TenantModelPreference.ModelMaxLength)] string Model);
