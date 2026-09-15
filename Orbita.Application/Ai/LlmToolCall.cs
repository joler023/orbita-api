namespace Orbita.Application.Ai;

/// <summary>
/// The model asking for a tool to be run. Normalized across providers: OpenAI-style
/// APIs return the arguments already as a JSON string while Ollama returns them as a
/// JSON object, and both adapters converge on the string form here so callers never
/// branch on which provider answered.
/// </summary>
/// <param name="Id">
/// The provider's id for this call, echoed back in <see cref="LlmMessage.ToolCallId"/>
/// when returning its result. Ollama does not issue ids, so its adapter synthesizes a
/// stable one — callers must not assume the value means anything to the provider.
/// </param>
/// <param name="Name">Which <see cref="LlmTool.Name"/> the model wants to run.</param>
/// <param name="ArgumentsJson">The arguments as a raw JSON object string, to be validated by the tool itself (ORB-C05).</param>
public sealed record LlmToolCall(
    string Id,
    string Name,
    string ArgumentsJson);
