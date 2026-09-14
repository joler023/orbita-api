using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orbita.Application.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Reads per-model prices from <c>Ai:Pricing:{model}:UsdPerMillion{Input|Output}Tokens</c>.
///
/// A model with no configured price costs 0. That is right for a locally hosted model
/// and wrong for a paid one, and the difference matters because <c>ai_runs.cost_usd</c>
/// is what ORB-A13's metering and ORB-A12's billing are computed from — so an unpriced
/// model that is clearly not local gets logged as a warning once rather than failing the
/// call. Refusing to answer a customer because a price is missing from configuration
/// would be the wrong trade; silently under-billing without saying so would be worse.
/// </summary>
public sealed class ConfigurationLlmPricing(IConfiguration configuration, ILogger<ConfigurationLlmPricing> logger)
    : ILlmPricing
{
    private readonly HashSet<string> _warnedModels = [];

    public decimal CostFor(string model, int tokensIn, int tokensOut)
    {
        var inputPrice = ReadPrice(model, "UsdPerMillionInputTokens");
        var outputPrice = ReadPrice(model, "UsdPerMillionOutputTokens");

        if (inputPrice == 0m && outputPrice == 0m)
        {
            WarnOnceIfProbablyPaid(model);
            return 0m;
        }

        return ((tokensIn * inputPrice) + (tokensOut * outputPrice)) / 1_000_000m;
    }

    private decimal ReadPrice(string model, string key)
        => decimal.TryParse(configuration[$"Ai:Pricing:{model}:{key}"], CultureInfo.InvariantCulture, out var price)
            ? price
            : 0m;

    /// <summary>
    /// A hosted model id is namespaced by its vendor (<c>openai/gpt-5.6-luna</c>);
    /// a local Ollama tag is not (<c>llama3.1</c>). It is a heuristic, but it separates
    /// "free because it runs here" from "priced at zero because nobody configured it".
    /// </summary>
    private void WarnOnceIfProbablyPaid(string model)
    {
        if (!model.Contains('/', StringComparison.Ordinal))
        {
            return;
        }

        lock (_warnedModels)
        {
            if (!_warnedModels.Add(model))
            {
                return;
            }
        }

        logger.LogWarning(
            "No price configured for LLM model {Model}; its runs will be recorded at zero cost. Set Ai:Pricing:{Model}:UsdPerMillionInputTokens and :UsdPerMillionOutputTokens.",
            model,
            model);
    }
}
