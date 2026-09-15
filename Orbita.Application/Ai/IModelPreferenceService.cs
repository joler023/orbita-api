using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C13: letting a tenant choose which model serves each task. Requires
/// <c>Permission.ManageAiAgents</c> — it changes both what customers are answered with and
/// what the organization is billed.
/// </summary>
public interface IModelPreferenceService
{
    /// <summary>
    /// Every task, with the model that would actually be used and whether that is the
    /// tenant's own choice or the deployment default. Returning both is the point: an
    /// owner deciding whether to switch needs to see what they are switching from.
    /// </summary>
    Task<IReadOnlyList<ModelPreferenceDto>> ListAsync(
        Guid tenantId,
        Guid callerUserId,
        string providerName,
        CancellationToken cancellationToken);

    /// <summary>Sets or replaces the tenant's choice for one task.</summary>
    Task<ModelPreferenceDto> SetAsync(
        Guid tenantId,
        Guid callerUserId,
        string providerName,
        LlmTask task,
        string model,
        CancellationToken cancellationToken);

    /// <summary>Drops the tenant's choice, falling back to the deployment default.</summary>
    Task ClearAsync(
        Guid tenantId,
        Guid callerUserId,
        string providerName,
        LlmTask task,
        CancellationToken cancellationToken);
}

/// <param name="Model">What a call for this task would actually use right now.</param>
/// <param name="IsTenantOverride">False when this is the deployment default rather than a choice.</param>
public sealed record ModelPreferenceDto(LlmTask Task, string Model, bool IsTenantOverride);
