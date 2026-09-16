using Orbita.Domain.Inbox;

namespace Orbita.UnitTests.Domain;

/// <summary>
/// ORB-C07's last criterion lives here: "una vez traspasada, el agente no vuelve a
/// intervenir salvo que un humano lo reactive". The assistant's own guard reads
/// <see cref="Conversation.IsWaitingForHuman"/>, so what these pin is that the flag
/// survives everything a customer can do next.
/// </summary>
public sealed class ConversationHandoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 15, 0, 0, TimeSpan.Zero);

    private static Conversation Open()
    {
        var conversation = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        conversation.RegisterInbound(Now, "hola");

        return conversation;
    }

    [Fact]
    public void Handing_over_queues_the_conversation_and_records_why()
    {
        var conversation = Open();

        conversation.RequestHumanHandoff(HandoffReason.CustomerAsked, "Pidió hablar con alguien.", Now);

        Assert.True(conversation.IsWaitingForHuman);
        Assert.Equal(HandoffReason.CustomerAsked, conversation.HandoffReason);
        Assert.Equal(Now, conversation.HandoffRequestedAt);
        Assert.Equal("Pidió hablar con alguien.", conversation.HandoffSummary);
        Assert.Equal(ConversationStatus.Pending, conversation.Status);

        // Nobody in particular owns it: picking a person is ORB-B15.
        Assert.Null(conversation.AssigneeId);
    }

    [Fact]
    public void The_first_reason_is_the_one_that_sticks()
    {
        var conversation = Open();
        conversation.RequestHumanHandoff(HandoffReason.CustomerAsked, "Pidió hablar con alguien.", Now);

        conversation.RequestHumanHandoff(HandoffReason.Frustration, "Se molestó.", Now.AddMinutes(5));

        // Relabelling "me pidieron un humano" as "parecía molesto" would lose the only
        // fact the person taking over can act on — and would restart the wait.
        Assert.Equal(HandoffReason.CustomerAsked, conversation.HandoffReason);
        Assert.Equal("Pidió hablar con alguien.", conversation.HandoffSummary);
        Assert.Equal(Now, conversation.HandoffRequestedAt);
    }

    [Fact]
    public void A_new_message_does_not_give_the_conversation_back_to_the_assistant()
    {
        var conversation = Open();
        conversation.RequestHumanHandoff(HandoffReason.CustomerAsked, null, Now);

        conversation.RegisterInbound(Now.AddMinutes(1), "¿hay alguien?");

        // Without this, Pending would flip back to Open and the assistant would answer the
        // customer's very next message — the criterion, broken by the ordinary path.
        Assert.Equal(ConversationStatus.Pending, conversation.Status);
        Assert.True(conversation.IsWaitingForHuman);

        // The rest of RegisterInbound still has to happen: a person does have to read it.
        Assert.Equal(2, conversation.UnreadCount);
        Assert.Equal(Now.AddMinutes(1) + Conversation.ServiceWindow, conversation.WindowExpiresAt);
    }

    [Fact]
    public void A_closed_conversation_still_reopens_normally()
    {
        // The guard is about handoffs, not about the reopening behaviour B03 relies on.
        var conversation = Open();
        conversation.Close(Now);

        conversation.RegisterInbound(Now.AddHours(1), "volví");

        Assert.Equal(ConversationStatus.Open, conversation.Status);
        Assert.Null(conversation.ClosedAt);
    }

    [Fact]
    public void A_human_can_give_it_back()
    {
        var conversation = Open();
        conversation.RequestHumanHandoff(HandoffReason.Frustration, "Estaba molesto.", Now);

        conversation.ReturnToAssistant();

        Assert.False(conversation.IsWaitingForHuman);
        Assert.Null(conversation.HandoffReason);
        Assert.Null(conversation.HandoffRequestedAt);
        Assert.Null(conversation.HandoffSummary);
        Assert.Equal(ConversationStatus.Open, conversation.Status);
    }

    [Fact]
    public void Giving_back_a_conversation_nobody_took_changes_nothing()
    {
        var conversation = Open();

        conversation.ReturnToAssistant();

        Assert.False(conversation.IsWaitingForHuman);
        Assert.Equal(ConversationStatus.Open, conversation.Status);
    }

    [Fact]
    public void A_summary_written_afterwards_fills_an_empty_note()
    {
        var conversation = Open();
        conversation.RequestHumanHandoff(HandoffReason.CustomerAsked, null, Now);

        Assert.True(conversation.AttachHandoffSummary("  Pidió una persona por un pedido equivocado.  "));
        Assert.Equal("Pidió una persona por un pedido equivocado.", conversation.HandoffSummary);
    }

    [Fact]
    public void A_summary_never_overwrites_the_assistants_own_note()
    {
        var conversation = Open();
        conversation.RequestHumanHandoff(HandoffReason.AgentDecision, "La nota del asistente.", Now);

        Assert.False(conversation.AttachHandoffSummary("Una nota generada después."));
        Assert.Equal("La nota del asistente.", conversation.HandoffSummary);
    }

    [Fact]
    public void A_summary_arriving_after_the_conversation_was_given_back_is_dropped()
    {
        // The model can take seconds; a person can return the conversation in between.
        var conversation = Open();
        conversation.RequestHumanHandoff(HandoffReason.CustomerAsked, null, Now);
        conversation.ReturnToAssistant();

        Assert.False(conversation.AttachHandoffSummary("Llegó tarde."));
        Assert.Null(conversation.HandoffSummary);
    }

    [Fact]
    public void A_long_summary_is_cut_to_the_column_it_lands_in()
    {
        var conversation = Open();

        conversation.RequestHumanHandoff(HandoffReason.AgentDecision, new string('a', 900), Now);

        Assert.Equal(Conversation.HandoffSummaryMaxLength, conversation.HandoffSummary!.Length);
    }
}
