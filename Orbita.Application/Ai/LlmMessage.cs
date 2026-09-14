namespace Orbita.Application.Ai;

/// <summary>
/// One turn of the conversation handed to a model. Provider-neutral: each adapter
/// translates this into whatever shape its own API wants.
/// </summary>
/// <param name="Role">Who authored this turn.</param>
/// <param name="Content">
/// The text. For a <see cref="LlmMessageRole.Tool"/> message this is the tool's
/// result, serialized as JSON by the caller.
/// </param>
/// <param name="ToolCallId">
/// Required on a <see cref="LlmMessageRole.Tool"/> message: which
/// <see cref="LlmToolCall.Id"/> this is the answer to. Null on every other role.
/// </param>
public sealed record LlmMessage(
    LlmMessageRole Role,
    string Content,
    string? ToolCallId = null)
{
    public static LlmMessage System(string content) => new(LlmMessageRole.System, content);

    public static LlmMessage User(string content) => new(LlmMessageRole.User, content);

    public static LlmMessage Assistant(string content) => new(LlmMessageRole.Assistant, content);

    public static LlmMessage Tool(string toolCallId, string resultJson)
        => new(LlmMessageRole.Tool, resultJson, toolCallId);
}
