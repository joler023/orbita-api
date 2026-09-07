using Microsoft.Extensions.Configuration;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Resolves <see cref="LlmTask"/> to a model id from configuration
/// (<c>Ai:Models:Classify</c> / <c>Draft</c> / <c>Embed</c>). This is ORB-C01's "uno
/// barato y rápido para clasificar, uno bueno para redactar" — the two can differ
/// without any call site knowing.
///
/// The per-tenant half of that requirement is ORB-C13's, and lands here: this
/// implementation ignores <c>tenantId</c> today and reads a single global mapping. When
/// C13 arrives it looks up the tenant's own choice first and falls back to this
/// mapping, and no caller changes.
///
/// <c>IConfiguration</c> is injected rather than read at registration time, per the rule
/// in CLAUDE.md that <c>WebApplicationFactory</c>-based tests only finish layering their
/// overrides once the host is built.
/// </summary>
internal sealed class ConfigurationLlmModelSelector(IConfiguration configuration) : ILlmModelSelector
{
    // Defaults target Ollama, which is what runs with no configuration and no account:
    // llama3.1 supports tool calling, and nomic-embed-text produces 768-dimension
    // vectors — the dimension knowledge_chunks.embedding is created with.
    private const string DefaultChatModel = "llama3.1";
    private const string DefaultEmbeddingModel = "nomic-embed-text";

    public string SelectModel(Guid tenantId, LlmTask task)
    {
        var configured = configuration[$"Ai:Models:{task}"];

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
