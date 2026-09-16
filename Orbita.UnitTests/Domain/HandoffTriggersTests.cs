using Orbita.Domain.Ai;
using Orbita.Domain.Inbox;

namespace Orbita.UnitTests.Domain;

/// <summary>
/// ORB-C07's two automatic triggers. The phrase list is deliberately short and specific,
/// and these are the cases that keep it that way: the expensive mistake is a false
/// positive, which takes a conversation away from the assistant until a person gives it
/// back — for a message that never asked for one.
/// </summary>
public sealed class HandoffTriggersTests
{
    private static IReadOnlyList<AgentConversationTurn> NoHistory => [];

    [Theory]
    [InlineData("quiero hablar con una persona")]
    [InlineData("Hola, ¿puedo hablar con alguien?")]
    [InlineData("PÁSAME CON UN ASESOR")]
    [InlineData("necesito una persona que me ayude")]
    [InlineData("hay alguien ahi?")]
    public void Asking_for_a_person_hands_the_conversation_over(string message)
        => Assert.Equal(HandoffReason.CustomerAsked, HandoffTriggers.Detect(message, NoHistory));

    [Theory]
    [InlineData("no me estás entendiendo")]
    [InlineData("esto no sirve para nada")]
    [InlineData("eres un bot inservible")]
    [InlineData("qué inútil")]
    public void Being_visibly_stuck_hands_the_conversation_over(string message)
        => Assert.Equal(HandoffReason.Frustration, HandoffTriggers.Detect(message, NoHistory));

    [Theory]
    [InlineData("¿a qué hora abren hoy?")]
    [InlineData("quiero una personalización del pedido")]
    [InlineData("la persona que me atendió ayer fue muy amable")]
    [InlineData("")]
    public void An_ordinary_message_is_answered_by_the_assistant(string message)
        => Assert.Null(HandoffTriggers.Detect(message, NoHistory));

    [Fact]
    public void Asking_for_a_person_wins_over_sounding_annoyed()
    {
        // Both match. The queue is labelled with the one a person can act on.
        var reason = HandoffTriggers.Detect("esto no sirve, quiero hablar con una persona", NoHistory);

        Assert.Equal(HandoffReason.CustomerAsked, reason);
    }

    [Fact]
    public void Saying_the_same_thing_three_times_counts_as_being_stuck()
    {
        // No angry word anywhere: this is the signal the phrase list cannot see, and the
        // reason repetition is judged objectively instead of guessed at from tone.
        IReadOnlyList<AgentConversationTurn> history =
        [
            new(FromAssistant: false, "¿hacen envíos a Cali?"),
            new(FromAssistant: true, "Hacemos envíos a todo el país."),
            new(FromAssistant: false, "¿Hacen envíos a Cali?"),
            new(FromAssistant: true, "Hacemos envíos a todo el país."),
        ];

        Assert.Equal(HandoffReason.Frustration, HandoffTriggers.Detect("¿hacen envíos a Cali?", history));
    }

    [Fact]
    public void Asking_twice_is_not_enough()
    {
        IReadOnlyList<AgentConversationTurn> history =
        [
            new(FromAssistant: false, "¿hacen envíos a Cali?"),
            new(FromAssistant: true, "Hacemos envíos a todo el país."),
        ];

        Assert.Null(HandoffTriggers.Detect("¿hacen envíos a Cali?", history));
    }

    [Fact]
    public void The_assistants_own_repetition_is_not_the_customers_frustration()
    {
        // Only the customer's turns count. An assistant repeating itself is ORB-C06's
        // loop guardrail, which is a different problem with a different answer.
        IReadOnlyList<AgentConversationTurn> history =
        [
            new(FromAssistant: true, "¿En qué te ayudo?"),
            new(FromAssistant: true, "¿En qué te ayudo?"),
        ];

        Assert.Null(HandoffTriggers.Detect("¿En qué te ayudo?", history));
    }

    [Fact]
    public void An_out_of_scope_subject_is_the_business_deciding_not_a_failure()
        => Assert.Equal(
            HandoffReason.OutOfScopeTopic,
            HandoffTriggers.ForGuardrail(GuardrailReason.OutOfScopeTopic));

    [Theory]
    [InlineData(GuardrailReason.LoopDetected)]
    [InlineData(GuardrailReason.TooManyRepliesInWindow)]
    [InlineData(GuardrailReason.EmptyReply)]
    [InlineData(GuardrailReason.RepeatedItself)]
    [InlineData(GuardrailReason.ReplyTooLong)]
    [InlineData(GuardrailReason.LeakedPromptScaffolding)]
    public void Everything_else_is_the_assistant_giving_up(GuardrailReason reason)
        => Assert.Equal(HandoffReason.AgentDecision, HandoffTriggers.ForGuardrail(reason));
}
