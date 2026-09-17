using System.Collections.Frozen;

namespace Orbita.Application.Ai;

/// <summary>
/// Reads the catalog off the providers that are actually registered, in the order they
/// were registered — so it cannot drift from what a call would really do, the way a
/// hand-written list would.
/// </summary>
public sealed class LlmProviderCatalog(IEnumerable<ILlmProvider> providers) : ILlmProviderCatalog
{
    /// <summary>
    /// Names for the two adapters ORB-C01 ships. A provider with no entry falls back to
    /// its own name rather than being hidden: an unnamed provider in a list is a smaller
    /// problem than a provider the screen pretends does not exist.
    /// </summary>
    private static readonly FrozenDictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["openai-compatible"] = "Servicio en la nube",
        ["ollama"] = "Modelos en este servidor",
    }.ToFrozenDictionary();

    private readonly IReadOnlyList<ILlmProvider> _providers = [.. providers];

    public IReadOnlyList<LlmProviderDto> List()
    {
        var primary = _providers.FirstOrDefault(provider => provider.IsConfigured)?.Name;

        return
        [
            .. _providers.Select(provider => new LlmProviderDto(
                provider.Name,
                DisplayNames.GetValueOrDefault(provider.Name, provider.Name),
                provider.IsConfigured,
                string.Equals(provider.Name, primary, StringComparison.OrdinalIgnoreCase))),
        ];
    }
}
