namespace Orbita.Application.Ai;

/// <summary>
/// One request for a model to produce a reply. The <paramref name="Model"/> is already
/// resolved by <see cref="ILlmModelSelector"/> before this record is built — providers
/// receive a concrete model id, never an <c>LlmTask</c>.
/// </summary>
/// <param name="Model">The resolved model id (e.g. <c>llama3.1</c>).</param>
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
    string Model,
    IReadOnlyList<LlmMessage> Messages,
    decimal Temperature,
    int MaxTokens,
    IReadOnlyList<LlmTool>? Tools = null);
