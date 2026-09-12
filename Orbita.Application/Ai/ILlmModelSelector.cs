using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// Resolves "which model do I use for this?" — ORB-C01's "el modelo se elige por tenant y
/// por tarea: uno barato y rápido para clasificar, uno bueno para redactar", and ORB-C13's
/// "configurable por tenant".
///
/// The answer depends on the provider as well as the task, because model ids are not
/// portable: <c>openai/gpt-5.6-luna</c> means something to a hosted gateway and nothing to
/// a local Ollama. Resolving per provider is what lets failover work across two providers
/// that do not share a model namespace.
///
/// Two layers: a tenant's own override wins, and the deployment-wide configuration is the
/// fallback. A tenant that has expressed no preference is not an error — most will not.
///
/// Asynchronous because the override lives in the database. Implementations are expected
/// to cache: this is called once per model call, which during indexing means once per
/// chunk.
/// </summary>
public interface ILlmModelSelector
{
    Task<string> SelectModelAsync(
        Guid tenantId,
        LlmTask task,
        string providerName,
        CancellationToken cancellationToken);
}
