using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// Resolves "which model do I use for this?" — ORB-C01's "el modelo se elige por tenant
/// y por tarea: uno barato y rápido para clasificar, uno bueno para redactar".
///
/// Implemented in Infrastructure because the answer comes from configuration, and this
/// layer never reads <c>IConfiguration</c> directly.
///
/// The <paramref name="tenantId"/> parameter is deliberately present from day one even
/// though the initial implementation ignores it: ORB-C13 ("selección de modelo por
/// tarea", configurable per tenant) is what fills it in, and adding the parameter later
/// would mean touching every call site.
/// </summary>
public interface ILlmModelSelector
{
    string SelectModel(Guid tenantId, LlmTask task);
}
