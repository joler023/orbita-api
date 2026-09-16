using Microsoft.Extensions.Configuration;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// Pins the shipped configuration itself, not the code that reads it: every model the
/// app is configured to actually call has to have a price.
///
/// <c>ConfigurationLlmPricing</c> answers 0 for an unpriced model on purpose — refusing
/// to answer a customer because a price is missing from configuration would be the wrong
/// trade. The cost of that choice is that a forgotten price is invisible: calls succeed,
/// <c>ai_runs.cost_usd</c> records 0, and ORB-A13's metering and ORB-C13's "the saving is
/// measurable" quietly under-report real spend. That is exactly what happened to
/// <c>openai/text-embedding-3-small</c>, which was assigned to <c>Embed</c> without ever
/// being priced.
///
/// These read the real appsettings files rather than a fixture's in-memory overrides,
/// because the thing under test *is* what ships.
/// </summary>
public sealed class ModelPricingConfigurationTests
{
    private static IConfiguration ShippedConfiguration()
    {
        var apiProject = Path.Combine(RepositoryRoot(), "Orbita.Api");

        return new ConfigurationBuilder()
            .SetBasePath(apiProject)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json")
            .Build();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orbita.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }

    /// <summary>
    /// A hosted model id is namespaced by its vendor (<c>openai/gpt-5.6-luna</c>); a local
    /// Ollama tag is not (<c>llama3.1</c>). Same heuristic ConfigurationLlmPricing uses to
    /// tell "free because it runs here" from "priced at zero because nobody configured it".
    /// </summary>
    public static TheoryData<string, string> AssignedHostedModels()
    {
        var configuration = ShippedConfiguration();
        var data = new TheoryData<string, string>();

        foreach (var provider in configuration.GetSection("Ai:Providers").GetChildren())
        {
            foreach (var task in provider.GetSection("Models").GetChildren())
            {
                var model = task.Value;

                if (!string.IsNullOrWhiteSpace(model) && model.Contains('/', StringComparison.Ordinal))
                {
                    data.Add($"{provider.Key}:{task.Key}", model);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AssignedHostedModels))]
    public void Every_hosted_model_assigned_to_a_task_has_a_price(string assignment, string model)
    {
        var configuration = ShippedConfiguration();
        var price = configuration.GetSection($"Ai:Pricing:{model}");

        Assert.True(
            price.Exists(),
            $"{assignment} usa '{model}', que no tiene precio en Ai:Pricing. Sus llamadas " +
            "se registrarían con cost_usd = 0.");

        // Input only: an embedding call has no output tokens, so requiring an output
        // price above zero would be wrong for the one model that needs this test most.
        Assert.True(
            decimal.TryParse(
                price["UsdPerMillionInputTokens"],
                System.Globalization.CultureInfo.InvariantCulture,
                out var inputPrice) && inputPrice > 0m,
            $"'{model}' tiene entrada en Ai:Pricing pero sin UsdPerMillionInputTokens usable.");

        Assert.NotNull(price["UsdPerMillionOutputTokens"]);
    }

    [Fact]
    public void The_embedding_model_is_priced_at_what_openrouter_actually_charges()
    {
        // Measured against the live API: 9 prompt tokens billed at USD 1.8e-07.
        var configuration = ShippedConfiguration();

        Assert.Equal(
            "0.02",
            configuration["Ai:Pricing:openai/text-embedding-3-small:UsdPerMillionInputTokens"]);
    }
}
