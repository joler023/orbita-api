using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Infrastructure.Ai;

namespace Orbita.IntegrationTests.Ai;

public sealed class ConfigurationLlmModelSelectorTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    private static ILlmModelSelector Build(params (string Key, string Value)[] settings)
        => new ConfigurationLlmModelSelector(Configuration(settings));

    [Fact]
    public void A_cheap_model_can_classify_while_a_better_one_drafts()
    {
        // This split is the whole point of ORB-C01's "por tarea" requirement.
        var selector = Build(
            ("Ai:Providers:openai-compatible:Models:Classify", "deepseek/deepseek-v4-flash"),
            ("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"));

        Assert.Equal("deepseek/deepseek-v4-flash", selector.SelectModel(Guid.NewGuid(), LlmTask.Classify, "openai-compatible"));
        Assert.Equal("openai/gpt-5.6-luna", selector.SelectModel(Guid.NewGuid(), LlmTask.Draft, "openai-compatible"));
    }

    [Fact]
    public void The_same_task_resolves_to_a_different_model_on_each_provider()
    {
        // Without this, failing over from a hosted gateway to a local Ollama would ask
        // Ollama for "openai/gpt-5.6-luna", which it has never heard of.
        var selector = Build(
            ("Ai:Providers:openai-compatible:Models:Draft", "openai/gpt-5.6-luna"),
            ("Ai:Providers:ollama:Models:Draft", "llama3.1"));

        var tenantId = Guid.NewGuid();

        Assert.Equal("openai/gpt-5.6-luna", selector.SelectModel(tenantId, LlmTask.Draft, "openai-compatible"));
        Assert.Equal("llama3.1", selector.SelectModel(tenantId, LlmTask.Draft, "ollama"));
    }

    [Fact]
    public void An_unconfigured_task_falls_back_to_the_models_this_project_actually_runs_on()
    {
        // A missing config line degrades to "the normal model" rather than to something
        // that does not exist.
        var selector = Build();

        Assert.Equal("openai/gpt-5.6-luna", selector.SelectModel(Guid.NewGuid(), LlmTask.Draft, "openai-compatible"));
        Assert.Equal("openai/text-embedding-3-small", selector.SelectModel(Guid.NewGuid(), LlmTask.Embed, "openai-compatible"));
    }

    [Fact]
    public void A_blank_configuration_value_is_treated_as_unset()
    {
        // appsettings.json ships these keys empty, the same way the Billing ones are.
        var selector = Build(("Ai:Providers:openai-compatible:Models:Embed", ""));

        Assert.Equal(
            "openai/text-embedding-3-small",
            selector.SelectModel(Guid.NewGuid(), LlmTask.Embed, "openai-compatible"));
    }

    [Fact]
    public void The_same_model_is_returned_for_every_tenant_until_ORB_C13_lands()
    {
        var selector = Build(("Ai:Providers:ollama:Models:Draft", "llama3.1"));

        Assert.Equal(
            selector.SelectModel(Guid.NewGuid(), LlmTask.Draft, "ollama"),
            selector.SelectModel(Guid.NewGuid(), LlmTask.Draft, "ollama"));
    }

    [Fact]
    public void Each_model_is_priced_on_its_own_rate()
    {
        // One gateway fronts several models at different prices, so a single
        // per-provider rate would mis-bill every call that did not use it.
        var pricing = new ConfigurationLlmPricing(
            Configuration(
                ("Ai:Pricing:openai/gpt-5.6-luna:UsdPerMillionInputTokens", "0.20"),
                ("Ai:Pricing:openai/gpt-5.6-luna:UsdPerMillionOutputTokens", "1.20"),
                ("Ai:Pricing:deepseek/deepseek-v4-flash:UsdPerMillionInputTokens", "0.14"),
                ("Ai:Pricing:deepseek/deepseek-v4-flash:UsdPerMillionOutputTokens", "0.28")),
            NullLogger<ConfigurationLlmPricing>.Instance);

        Assert.Equal(1.40m, pricing.CostFor("openai/gpt-5.6-luna", 1_000_000, 1_000_000));
        Assert.Equal(0.42m, pricing.CostFor("deepseek/deepseek-v4-flash", 1_000_000, 1_000_000));
    }

    [Fact]
    public void A_locally_hosted_model_costs_nothing()
    {
        var pricing = new ConfigurationLlmPricing(Configuration(), NullLogger<ConfigurationLlmPricing>.Instance);

        Assert.Equal(0m, pricing.CostFor("llama3.1", 1_000_000, 1_000_000));
    }
}
