namespace Orbita.Application.Ai;

/// <summary>
/// A tool offered to the model for function calling — ORB-C05's
/// <c>crear_oportunidad</c>, <c>agendar_cita</c>, <c>escalar_a_humano</c> and friends.
/// This is only the *declaration* sent to the provider; executing a call the model
/// makes back is ORB-C05's job, not this port's.
/// </summary>
/// <param name="Name">The function name the model will use when calling it.</param>
/// <param name="Description">
/// What the tool does, in the language the model should reason about it in. This is
/// prompt surface, not documentation — it is the main thing that decides whether the
/// model picks the right tool.
/// </param>
/// <param name="ParametersJsonSchema">
/// A JSON Schema object describing the arguments, as a raw JSON string. Kept as a
/// string rather than a <c>JsonElement</c> or <c>object</c> so the Application layer
/// stays free of a serializer dependency, and each adapter embeds it verbatim into its
/// own request body — both providers happen to want plain JSON Schema here.
/// </param>
public sealed record LlmTool(
    string Name,
    string Description,
    string ParametersJsonSchema);
