using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

public sealed class ModelPreferenceService(
    ITenantModelPreferenceRepository preferences,
    ILlmModelSelector modelSelector,
    IModelPreferenceCacheInvalidator cacheInvalidator,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IModelPreferenceService
{
    public async Task<IReadOnlyList<ModelPreferenceDto>> ListAsync(
        Guid tenantId,
        Guid callerUserId,
        string providerName,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiModels, cancellationToken);

        var overrides = await LoadAsync(tenantId, cancellationToken);
        var result = new List<ModelPreferenceDto>();

        foreach (var task in Enum.GetValues<LlmTask>())
        {
            // Asked through the selector rather than read from config here, so the list
            // can never disagree with what a real call would resolve to.
            var model = await modelSelector.SelectModelAsync(tenantId, task, providerName, cancellationToken);
            var isOverride = overrides.Any(row => Matches(row, providerName, task));

            result.Add(new ModelPreferenceDto(task, model, isOverride));
        }

        return result;
    }

    public async Task<ModelPreferenceDto> SetAsync(
        Guid tenantId,
        Guid callerUserId,
        string providerName,
        LlmTask task,
        string model,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiModels, cancellationToken);

        var existing = (await LoadAsync(tenantId, cancellationToken))
            .SingleOrDefault(row => Matches(row, providerName, task));

        if (existing is null)
        {
            preferences.Add(TenantModelPreference.Create(tenantId, providerName, task, model, timeProvider.GetUtcNow()));
        }
        else
        {
            existing.ChangeModel(model, timeProvider.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Evicted after the commit, so a reader that raced the write cannot repopulate the
        // cache with the old value.
        cacheInvalidator.Invalidate(tenantId);

        return new ModelPreferenceDto(task, model.Trim(), IsTenantOverride: true);
    }

    public async Task ClearAsync(
        Guid tenantId,
        Guid callerUserId,
        string providerName,
        LlmTask task,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiModels, cancellationToken);

        var existing = (await LoadAsync(tenantId, cancellationToken))
            .SingleOrDefault(row => Matches(row, providerName, task));

        // Clearing something already absent is the desired end state, not an error.
        if (existing is null)
        {
            return;
        }

        preferences.Remove(existing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        cacheInvalidator.Invalidate(tenantId);
    }

    private Task<IReadOnlyList<TenantModelPreference>> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
        => unitOfWork.QueryInTenantScopeAsync(
            ct => preferences.ListByTenantAsync(tenantId, ct),
            cancellationToken);

    private static bool Matches(TenantModelPreference row, string providerName, LlmTask task)
        => row.Task == task && string.Equals(row.ProviderName, providerName, StringComparison.OrdinalIgnoreCase);
}
