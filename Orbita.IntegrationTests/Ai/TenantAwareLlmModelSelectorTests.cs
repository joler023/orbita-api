using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Infrastructure.Ai;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C13's two-layer resolution. The repository and unit of work are stubbed so these
/// stay fast and Docker-free; the real database path is covered by the API tests.
/// </summary>
public sealed class TenantAwareLlmModelSelectorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly StubPreferenceRepository _preferences = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private ILlmModelSelector Build(params (string Key, string Value)[] settings)
        => new TenantAwareLlmModelSelector(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
                .Build(),
            _preferences,
            new PassThroughUnitOfWork(),
            _cache);

    private Task<string> SelectAsync(ILlmModelSelector selector, LlmTask task, string provider = "openai-compatible")
        => selector.SelectModelAsync(TenantId, task, provider, CancellationToken.None);

    [Fact]
    public async Task A_cheap_model_can_classify_while_a_better_one_drafts()
    {
        // The split ORB-C01 asks for, and what ORB-C13's saving is measured against.
        var selector = Build(
            ("Ai:Providers:openai-compatible:Models:Classify", "deepseek/deepseek-v4-flash"),
            ("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"));

        Assert.Equal("deepseek/deepseek-v4-flash", await SelectAsync(selector, LlmTask.Classify));
        Assert.Equal("openai/gpt-5.6-luna", await SelectAsync(selector, LlmTask.Draft));
    }

    [Fact]
    public async Task The_same_task_resolves_to_a_different_model_on_each_provider()
    {
        // Without this, failing over to a local Ollama would ask it for a gateway's model
        // id, which it has never heard of.
        var selector = Build(
            ("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"),
            ("Ai:Providers:ollama:Models:Draft", "llama3.1"));

        Assert.Equal("openai/gpt-5.6-luna", await SelectAsync(selector, LlmTask.Draft));
        Assert.Equal("llama3.1", await SelectAsync(selector, LlmTask.Draft, "ollama"));
    }

    [Fact]
    public async Task A_tenants_own_choice_beats_the_deployment_default()
    {
        // This is ORB-C13: "configurable por tenant".
        _preferences.Add(TenantId, "openai-compatible", LlmTask.Draft, "google/gemini-3.5-flash-lite");

        var selector = Build(("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"));

        Assert.Equal("google/gemini-3.5-flash-lite", await SelectAsync(selector, LlmTask.Draft));
    }

    [Fact]
    public async Task An_override_for_one_task_leaves_the_others_on_the_default()
    {
        // A tenant overrides what it cares about; an absent row means "use the default",
        // which is why preferences are rows rather than a column of nullables.
        _preferences.Add(TenantId, "openai-compatible", LlmTask.Draft, "google/gemini-3.5-flash-lite");

        var selector = Build(
            ("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"),
            ("Ai:Providers:openai-compatible:Models:Classify", "deepseek/deepseek-v4-flash"));

        Assert.Equal("google/gemini-3.5-flash-lite", await SelectAsync(selector, LlmTask.Draft));
        Assert.Equal("deepseek/deepseek-v4-flash", await SelectAsync(selector, LlmTask.Classify));
    }

    [Fact]
    public async Task An_override_applies_only_to_the_provider_it_names()
    {
        _preferences.Add(TenantId, "openai-compatible", LlmTask.Draft, "google/gemini-3.5-flash-lite");

        var selector = Build(("Ai:Providers:ollama:Models:Draft", "llama3.1"));

        Assert.Equal("llama3.1", await SelectAsync(selector, LlmTask.Draft, "ollama"));
    }

    [Fact]
    public async Task Another_tenants_override_never_leaks()
    {
        _preferences.Add(Guid.NewGuid(), "openai-compatible", LlmTask.Draft, "modelo-ajeno");

        var selector = Build(("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"));

        Assert.Equal("openai/gpt-5.6-luna", await SelectAsync(selector, LlmTask.Draft));
    }

    [Fact]
    public async Task The_overrides_are_read_once_and_then_cached()
    {
        // Indexing a document selects a model once per chunk; without the cache a
        // fifty-page PDF would be hundreds of round trips for a value that never changes
        // mid-run.
        var selector = Build(("Ai:Providers:openai-compatible:Models:Embed", "openai/text-embedding-3-small"));

        for (var i = 0; i < 25; i++)
        {
            await SelectAsync(selector, LlmTask.Embed);
        }

        Assert.Equal(1, _preferences.ReadCount);
    }

    [Fact]
    public async Task Invalidating_makes_a_change_visible_immediately()
    {
        // Otherwise "I changed it and nothing happened" becomes a support question.
        var selector = Build(("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"));
        Assert.Equal("openai/gpt-5.6-luna", await SelectAsync(selector, LlmTask.Draft));

        _preferences.Add(TenantId, "openai-compatible", LlmTask.Draft, "google/gemini-3.5-flash-lite");
        TenantAwareLlmModelSelector.Invalidate(_cache, TenantId);

        Assert.Equal("google/gemini-3.5-flash-lite", await SelectAsync(selector, LlmTask.Draft));
    }

    [Fact]
    public async Task An_unconfigured_task_falls_back_to_a_model_that_exists()
    {
        var selector = Build();

        Assert.Equal("openai/gpt-5.6-luna", await SelectAsync(selector, LlmTask.Draft));
        Assert.Equal("openai/text-embedding-3-small", await SelectAsync(selector, LlmTask.Embed));
    }

    [Fact]
    public async Task A_blank_configuration_value_is_treated_as_unset()
    {
        // appsettings.json ships these keys empty, the same way the Billing ones are.
        var selector = Build(("Ai:Providers:openai-compatible:Models:Embed", ""));

        Assert.Equal("openai/text-embedding-3-small", await SelectAsync(selector, LlmTask.Embed));
    }

    private sealed class StubPreferenceRepository : ITenantModelPreferenceRepository
    {
        private readonly List<TenantModelPreference> _rows = [];

        public int ReadCount { get; private set; }

        public void Add(Guid tenantId, string provider, LlmTask task, string model)
            => _rows.Add(TenantModelPreference.Create(tenantId, provider, task, model, Now));

        public void Add(TenantModelPreference preference) => _rows.Add(preference);

        public void Remove(TenantModelPreference preference) => _rows.Remove(preference);

        public Task<IReadOnlyList<TenantModelPreference>> ListByTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<TenantModelPreference>>(
                _rows.Where(row => row.TenantId == tenantId).ToList());
        }
    }

    /// <summary>Runs the query without a transaction; the real RLS behaviour is covered by the API tests.</summary>
    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TResult> QueryInTenantScopeAsync<TResult>(
            Func<CancellationToken, Task<TResult>> query,
            CancellationToken cancellationToken) => query(cancellationToken);

        public Task ExecuteInTenantScopeAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);

        public Task ExecuteAndSaveInTenantScopeAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);

        public Task<TResult> QueryInUserScopeAsync<TResult>(
            Guid userId,
            Func<CancellationToken, Task<TResult>> query,
            CancellationToken cancellationToken) => query(cancellationToken);
    }
}

public sealed class LlmPricingTests
{
    [Fact]
    public void Each_model_is_priced_on_its_own_rate()
    {
        // One gateway fronts several models at different prices, so a single per-provider
        // rate would mis-bill every call that did not use it — and ORB-C13's saving would
        // be invisible.
        var pricing = new ConfigurationLlmPricing(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ai:Pricing:openai/gpt-5.6-luna:UsdPerMillionInputTokens"] = "0.20",
                    ["Ai:Pricing:openai/gpt-5.6-luna:UsdPerMillionOutputTokens"] = "1.20",
                    ["Ai:Pricing:deepseek/deepseek-v4-flash:UsdPerMillionInputTokens"] = "0.14",
                    ["Ai:Pricing:deepseek/deepseek-v4-flash:UsdPerMillionOutputTokens"] = "0.28",
                })
                .Build(),
            NullLogger<ConfigurationLlmPricing>.Instance);

        Assert.Equal(1.40m, pricing.CostFor("openai/gpt-5.6-luna", 1_000_000, 1_000_000));
        Assert.Equal(0.42m, pricing.CostFor("deepseek/deepseek-v4-flash", 1_000_000, 1_000_000));

        // And the cheap one really is cheaper, which is the whole premise of ORB-C13.
        Assert.True(
            pricing.CostFor("deepseek/deepseek-v4-flash", 1_000, 1_000)
                < pricing.CostFor("openai/gpt-5.6-luna", 1_000, 1_000));
    }

    [Fact]
    public void A_locally_hosted_model_costs_nothing()
    {
        var pricing = new ConfigurationLlmPricing(
            new ConfigurationBuilder().Build(),
            NullLogger<ConfigurationLlmPricing>.Instance);

        Assert.Equal(0m, pricing.CostFor("llama3.1", 1_000_000, 1_000_000));
    }
}
