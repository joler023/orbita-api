using Microsoft.Extensions.Configuration;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Infrastructure.Ai;

namespace Orbita.IntegrationTests.Ai;

public sealed class ConfigurationLlmModelSelectorTests
{
    private static ILlmModelSelector Build(params (string Key, string Value)[] settings)
        => new ConfigurationLlmModelSelector(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
                .Build());

    [Fact]
    public void A_cheap_model_can_classify_while_a_better_one_drafts()
    {
        // This split is the whole point of ORB-C01's "por tarea" requirement.
        var selector = Build(
            ("Ai:Models:Classify", "qwen2.5:1.5b"),
            ("Ai:Models:Draft", "llama3.1:70b"));

        Assert.Equal("qwen2.5:1.5b", selector.SelectModel(Guid.NewGuid(), LlmTask.Classify));
        Assert.Equal("llama3.1:70b", selector.SelectModel(Guid.NewGuid(), LlmTask.Draft));
    }

    [Fact]
    public void Unconfigured_tasks_fall_back_to_models_that_run_locally()
    {
        // A developer with Ollama installed and no configuration at all still gets a
        // working setup.
        var selector = Build();

        Assert.Equal("llama3.1", selector.SelectModel(Guid.NewGuid(), LlmTask.Draft));
        Assert.Equal("nomic-embed-text", selector.SelectModel(Guid.NewGuid(), LlmTask.Embed));
    }

    [Fact]
    public void A_blank_configuration_value_is_treated_as_unset()
    {
        // appsettings.json ships these keys empty, the same way the Billing ones are.
        var selector = Build(("Ai:Models:Embed", ""));

        Assert.Equal("nomic-embed-text", selector.SelectModel(Guid.NewGuid(), LlmTask.Embed));
    }

    [Fact]
    public void The_same_model_is_returned_for_every_tenant_until_ORB_C13_lands()
    {
        var selector = Build(("Ai:Models:Draft", "llama3.1"));

        Assert.Equal(
            selector.SelectModel(Guid.NewGuid(), LlmTask.Draft),
            selector.SelectModel(Guid.NewGuid(), LlmTask.Draft));
    }
}
