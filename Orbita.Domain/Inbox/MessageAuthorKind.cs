namespace Orbita.Domain.Inbox;

/// <summary>
/// Who wrote a message, as a value rather than something a reader has to infer.
///
/// Derived from the columns, not stored: <c>sent_by_user_id</c> and <c>ai_run_id</c>
/// already say this between them. What it replaces is the inference
/// "<c>sentByUserId == null</c> means the assistant wrote it", which is true today only
/// because the assistant is the one non-human sender that exists. The moment a scheduled
/// template, an automation or a system notice sends something, that inference starts
/// mislabelling messages without failing — and a wrong author on a customer conversation
/// is the kind of bug nobody reports because it looks like it works.
/// </summary>
public enum MessageAuthorKind
{
    /// <summary>A person on the team, or the customer.</summary>
    Human,

    /// <summary>An AI assistant (ORB-C04). Always carries an <c>ai_run_id</c>.</summary>
    AiAgent,

    /// <summary>Sent by the product itself, with no person and no assistant behind it.</summary>
    System,
}
