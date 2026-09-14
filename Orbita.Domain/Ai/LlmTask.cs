namespace Orbita.Domain.Ai;

/// <summary>
/// What a model call is being made *for* (ORB-C01: "el modelo se elige por tenant y
/// por tarea: uno barato y rápido para clasificar, uno bueno para redactar").
///
/// Callers name the task, never a concrete model — resolving the task to a model id is
/// <c>ILlmModelSelector</c>'s job, so swapping models (or making the choice
/// per-tenant, which is ORB-C13) never touches call sites.
/// </summary>
public enum LlmTask
{
    /// <summary>Cheap, fast, low-stakes: intent classification, routing, guardrail checks.</summary>
    Classify,

    /// <summary>The expensive one: composing a reply a customer will actually read.</summary>
    Draft,

    /// <summary>Turning text into a vector for the knowledge base (ORB-C02/C03).</summary>
    Embed,
}
