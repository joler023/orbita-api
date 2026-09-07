using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// Resolves "which model do I use for this?" — ORB-C01's "el modelo se elige por tenant
/// y por tarea: uno barato y rápido para clasificar, uno bueno para redactar".
///
/// The answer depends on the provider as well as the task, because model ids are not
/// portable: <c>openai/gpt-5.6-luna</c> means something to an OpenAI-compatible gateway
/// and nothing at all to a local Ollama, which knows <c>llama3.1</c>. Resolving per
/// provider is what lets failover actually work across two providers that do not share
/// a model namespace — the exact pairing this project runs (Ollama locally, a hosted
/// gateway in production).
///
/// Implemented in Infrastructure because the answer comes from configuration, and this
/// layer never reads <c>IConfiguration</c> directly.
///
/// <paramref name="tenantId"/> is unused by the initial implementation but present from
/// day one: ORB-C13 makes the choice per tenant, and adding the parameter later would
/// mean touching every adapter.
/// </summary>
public interface ILlmModelSelector
{
    string SelectModel(Guid tenantId, LlmTask task, string providerName);
}
