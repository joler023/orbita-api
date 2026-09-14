using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// ORB-C13. Resolves a model in two layers: the tenant's own override first, then the
/// deployment-wide setting at <c>Ai:Providers:{provider}:Models:{task}</c>.
///
/// <para><b>Why it caches.</b> Selection happens on every model call, and indexing a
/// document makes one call per chunk — a fifty-page PDF is hundreds. Reading a tenant's
/// overrides from Postgres that many times would turn a config lookup into the dominant
/// cost of indexing. The whole set is cached per tenant for
/// <see cref="CacheDuration"/>, and writes evict it, so a change takes effect immediately
/// for the person who made it and within a minute everywhere else.</para>
///
/// <para><b>Why the fallbacks exist at all.</b> A provider with no <c>Models</c> section
/// configured degrades to the model this project actually runs on rather than to something
/// that does not exist — a missing config line should not become a 404 from the gateway.</para>
///
/// <c>IConfiguration</c> is injected rather than read at registration time, per the rule in
/// CLAUDE.md that <c>WebApplicationFactory</c>-based tests only finish layering their
/// overrides once the host is built.
/// </summary>
public sealed class TenantAwareLlmModelSelector(
    IConfiguration configuration,
    ITenantModelPreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    IMemoryCache cache) : ILlmModelSelector
{
    /// <summary>
    /// Short on purpose. Model choice is not hot data and a stale minute is harmless, but
    /// a long window would make "I changed it and nothing happened" a support question.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private const string DefaultChatModel = "openai/gpt-5.6-luna";
    private const string DefaultEmbeddingModel = "openai/text-embedding-3-small";

    public async Task<string> SelectModelAsync(
        Guid tenantId,
        LlmTask task,
        string providerName,
        CancellationToken cancellationToken)
    {
        var overrides = await GetOverridesAsync(tenantId, cancellationToken);

        if (overrides.TryGetValue(Key(providerName, task), out var chosen))
        {
            return chosen;
        }

        var configured = configuration[$"Ai:Providers:{providerName}:Models:{task}"];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return task switch
        {
            LlmTask.Embed => DefaultEmbeddingModel,
            LlmTask.Classify or LlmTask.Draft => DefaultChatModel,
            _ => throw new ArgumentOutOfRangeException(nameof(task), task, "Unmapped LLM task."),
        };
    }

    /// <summary>Drops a tenant's cached overrides so the next call re-reads them.</summary>
    public static void Invalidate(IMemoryCache cache, Guid tenantId) => cache.Remove(CacheKey(tenantId));

    private async Task<IReadOnlyDictionary<string, string>> GetOverridesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey(tenantId), out IReadOnlyDictionary<string, string>? cached) && cached is not null)
        {
            return cached;
        }

        // tenant_model_preferences is RLS'd like every tenant-scoped table, and this read
        // is not followed by a save, so it needs its own tenant-scoped transaction — the
        // trap CLAUDE.md documents for ORB-A15.
        var rows = await unitOfWork.QueryInTenantScopeAsync(
            ct => preferences.ListByTenantAsync(tenantId, ct),
            cancellationToken);

        var overrides = rows.ToDictionary(
            row => Key(row.ProviderName, row.Task),
            row => row.Model,
            StringComparer.OrdinalIgnoreCase);

        cache.Set(CacheKey(tenantId), (IReadOnlyDictionary<string, string>)overrides, CacheDuration);

        return overrides;
    }

    private static string CacheKey(Guid tenantId) => $"ai:model-preferences:{tenantId:D}";

    private static string Key(string providerName, LlmTask task) => $"{providerName}|{task}";
}
