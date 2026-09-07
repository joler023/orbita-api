using Microsoft.Extensions.Configuration;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Resolves <see cref="LlmTask"/> to a model id from
/// <c>Ai:Providers:{provider}:Models:{task}</c>.
///
/// Keyed by provider as well as task because model ids are not portable: a gateway
/// knows <c>openai/gpt-5.6-luna</c>, a local Ollama knows <c>llama3.1</c>, and asking
/// either for the other's id is a 404. Without this, ORB-C01's failover requirement
/// would only work between providers that happen to share a model namespace.
///
/// The per-tenant half of "el modelo se elige por tenant y por tarea" is ORB-C13's and
/// lands here: this implementation ignores <c>tenantId</c> and reads the deployment-wide
/// mapping. When C13 arrives it looks up the tenant's own choice first and falls back to
/// this, and no adapter changes.
///
/// <c>IConfiguration</c> is injected rather than read at registration time, per the rule
/// in CLAUDE.md that <c>WebApplicationFactory</c>-based tests only finish layering their
/// overrides once the host is built.
/// </summary>
public sealed class ConfigurationLlmModelSelector(IConfiguration configuration) : ILlmModelSelector
{
    // Last-resort fallbacks, used only when a provider has no Models section configured
    // at all. They name the hosted models this project actually runs on, so a missing
    // config line degrades to "the normal model" rather than to something that does not
    // exist.
    private const string DefaultChatModel = "openai/gpt-5.6-luna";
    private const string DefaultEmbeddingModel = "openai/text-embedding-3-small";

    public string SelectModel(Guid tenantId, LlmTask task, string providerName)
    {
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
}
