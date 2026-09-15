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
    string? ToolCallId = null,
    IReadOnlyList<LlmToolCall>? ToolCalls = null)
{
    /// <summary>
    /// The assistant turn in which the model asked for tools. It has to be sent back
    /// verbatim before the tool results: the OpenAI chat format rejects a <c>tool</c>
    /// message that does not answer a <c>tool_calls</c> entry in the turn before it, so a
    /// second round of a tool loop would fail against a real provider without it — and
    /// would pass against any fake that does not check the pairing.
    /// </summary>
    public static LlmMessage AssistantToolCalls(IReadOnlyList<LlmToolCall> toolCalls)
        => new(LlmMessageRole.Assistant, string.Empty, ToolCalls: toolCalls);

    public static LlmMessage System(string content) => new(LlmMessageRole.System, content);

    public static LlmMessage User(string content) => new(LlmMessageRole.User, content);

    public static LlmMessage Assistant(string content) => new(LlmMessageRole.Assistant, content);

    public static LlmMessage Tool(string toolCallId, string resultJson)
        => new(LlmMessageRole.Tool, resultJson, toolCallId);
}
