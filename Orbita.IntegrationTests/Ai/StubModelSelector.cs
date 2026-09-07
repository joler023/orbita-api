using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.IntegrationTests.Ai;

/// <summary>Fixed answers, so an adapter test pins wire format rather than configuration.</summary>
internal sealed class StubModelSelector(string chatModel, string embeddingModel) : ILlmModelSelector
{
    public List<string> RequestedProviders { get; } = [];

    public string SelectModel(Guid tenantId, LlmTask task, string providerName)
    {
        RequestedProviders.Add(providerName);
        return task is LlmTask.Embed ? embeddingModel : chatModel;
    }
}

/// <summary>Flat per-million rates, independent of which model was used.</summary>
internal sealed class StubLlmPricing(decimal usdPerMillionInput, decimal usdPerMillionOutput) : ILlmPricing
{
    public decimal CostFor(string model, int tokensIn, int tokensOut)
        => ((tokensIn * usdPerMillionInput) + (tokensOut * usdPerMillionOutput)) / 1_000_000m;
}
