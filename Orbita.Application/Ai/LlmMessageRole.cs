namespace Orbita.Application.Ai;

/// <summary>Who authored a turn in the conversation sent to a model.</summary>
public enum LlmMessageRole
{
    /// <summary>The agent's standing instructions. Composed by the backend, never shown to the end user.</summary>
    System,

    /// <summary>The customer writing in from WhatsApp/Instagram, or the operator in the ORB-C11 test bench.</summary>
    User,

    /// <summary>A previous reply from the model, including one that only asked for a tool call.</summary>
    Assistant,

    /// <summary>The result of a tool the model asked for, fed back so it can continue (ORB-C05).</summary>
    Tool,
}
