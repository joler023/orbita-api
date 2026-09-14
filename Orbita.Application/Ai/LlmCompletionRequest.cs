using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// One request for a model to produce a reply.
///
/// It names a <see cref="LlmTask"/>, never a model. Which concrete model serves the
/// task is resolved by each adapter through <see cref="ILlmModelSelector"/>, because
/// model ids are provider-specific — if the caller pinned one, failing over from a
/// hosted gateway to a local Ollama would ask Ollama for a model it has never heard of.
/// </summary>
/// <param name="TenantId">Whose request this is, so ORB-C13 can vary the model per tenant.</param>
/// <param name="Task">What the call is for; drives model selection.</param>
/// <param name="Messages">The conversation so far, oldest first.</param>
/// <param name="Temperature">
/// Sampling temperature. Product surfaces never expose this number — ORB-C10's UI asks
/// "¿qué tan creativo quieres que sea?" and the Application layer maps that answer onto
/// a value here.
/// </param>
/// <param name="MaxTokens">Upper bound on the reply length.</param>
/// <param name="Tools">
/// Tools the model may call. Empty means plain text generation — the adapters omit the
/// tools field entirely rather than sending an empty array, since some providers reject
/// that.
/// </param>
public sealed record LlmCompletionRequest(
    Guid TenantId,
    LlmTask Task,
    IReadOnlyList<LlmMessage> Messages,
    decimal Temperature,
    int MaxTokens,
    IReadOnlyList<LlmTool>? Tools = null);
