namespace Orbita.Application.Ai;

/// <summary>
/// Which model providers this deployment has, for the screen that chooses a model per
/// task (ORB-C13).
///
/// It exists because <c>/api/tenants/{t}/ai-models/{providerName}</c> takes a provider
/// name in the route and nothing told anyone what to put there. The frontend asked for a
/// catalog rather than a hardcoded name, with the same argument that won for
/// <c>GET /api/ai-tools</c>: a name baked into a screen means a frontend release the day
/// a provider is added or renamed.
/// </summary>
public interface ILlmProviderCatalog
{
    /// <summary>
    /// In failover order, primary first — the same order a call actually tries them in.
    /// </summary>
    IReadOnlyList<LlmProviderDto> List();
}

/// <param name="Name">What goes in the route. Lowercase and stable.</param>
/// <param name="DisplayName">
/// For the screen. Deliberately free of model jargon, like every other string ORB-C10's
/// screens render.
/// </param>
/// <param name="IsConfigured">
/// Whether it has the settings it needs (an API key, a base URL) to be called at all. An
/// unconfigured provider is skipped at runtime rather than tried and failed, so a
/// preference pointing at one would never take effect — which is worth showing rather
/// than letting someone save it and wonder.
/// </param>
/// <param name="IsPrimary">
/// Whether it is the one tried first among the configured ones. Null preference means
/// this is the provider actually answering today.
/// </param>
public sealed record LlmProviderDto(
    string Name,
    string DisplayName,
    bool IsConfigured,
    bool IsPrimary);
