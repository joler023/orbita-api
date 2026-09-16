using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Domain;

/// <summary>
/// ORB-C06's rules, with no model and no database. The matching tests are the ones that
/// matter most: every false positive here silences an assistant on an ordinary message,
/// and nobody can tell why from the outside.
/// </summary>
public sealed class AgentGuardrailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static AiAgent AgentBlocking(params string[] topics)
    {
        var agent = AiAgent.Create(Guid.NewGuid(), "Asistente", "Amable.", "Ayuda.", AgentStyle.Default, Now);
        agent.SetGuardrails(topics, AiAgent.DefaultOutOfScopeReply);

        return agent;
    }

    [Theory]
    [InlineData("talla", "No me carga la pantalla del catálogo")]
    [InlineData("precio", "Apreciamos mucho la atención")]
    [InlineData("cita", "Felicitaciones por el nuevo local")]
    [InlineData("dosis", "Quiero hacer una sobredosis de pan")]
    public void A_topic_only_fires_on_the_whole_word(string topic, string message)
    {
        // Substring matching would fire on every one of these and answer the customer's
        // ordinary message with "eso lo ve el equipo". The failure is silence, not an error.
        Assert.Null(AgentGuardrails.MatchedBlockedTopic(AgentBlocking(topic), message));
    }

    [Theory]
    [InlineData("diagnóstico", "¿me pueden dar un diagnostico?")]
    [InlineData("DOSIS", "qué dosis tomo")]
    [InlineData("dosis recomendada", "¿Cuál es la dosis  recomendada?")]
    public void Matching_ignores_accents_case_and_extra_spaces(string topic, string message)
        => Assert.Equal(topic, AgentGuardrails.MatchedBlockedTopic(AgentBlocking(topic), message));

    [Fact]
    public void The_accepted_cost_of_whole_word_matching_is_that_plurals_need_their_own_entry()
    {
        // Pinned on purpose so nobody "fixes" it back to substring: erring toward not
        // blocking is recoverable, erring toward blocking leaves the assistant mute.
        Assert.Null(AgentGuardrails.MatchedBlockedTopic(AgentBlocking("precio"), "¿Tienen lista de precios?"));
        Assert.NotNull(AgentGuardrails.MatchedBlockedTopic(AgentBlocking("precio", "precios"), "¿Tienen lista de precios?"));
    }

    [Fact]
    public void Blank_topics_are_dropped_because_an_empty_topic_would_match_everything()
    {
        var agent = AgentBlocking("  ", "", "dosis", "Dosis ");

        Assert.Equal(["dosis"], agent.BlockedTopics);
        Assert.Null(AgentGuardrails.MatchedBlockedTopic(agent, "hola"));
    }

    [Fact]
    public void More_than_fifty_topics_is_refused()
    {
        var agent = AiAgent.Create(Guid.NewGuid(), "Asistente", "Amable.", "Ayuda.", AgentStyle.Default, Now);
        var topics = Enumerable.Range(0, AiAgent.MaxBlockedTopics + 1).Select(i => $"tema{i}").ToList();

        Assert.Throws<ArgumentException>(() => agent.SetGuardrails(topics, AiAgent.DefaultOutOfScopeReply));
    }

    [Fact]
    public void The_default_reply_promises_no_handoff_that_does_not_exist()
    {
        // ORB-C07 is not built: nobody is notified and nobody will write. A default that
        // said so would lie to every customer of every tenant that never edits it.
        Assert.DoesNotContain("aviso", AiAgent.DefaultOutOfScopeReply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("te escriben", AiAgent.DefaultOutOfScopeReply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Three_assistant_turns_in_a_row_is_a_loop()
    {
        var history = new List<AgentConversationTurn>
        {
            new(false, "hola"),
            new(true, "uno"),
            new(true, "dos"),
            new(true, "tres"),
        };

        var verdict = AgentGuardrails.InspectIncoming(AgentBlocking(), "¿sigues ahí?", 0, history);

        Assert.Equal(GuardrailReason.LoopDetected, verdict.Reason);
    }

    [Fact]
    public void A_reply_past_what_whatsapp_accepts_is_blocked()
    {
        var verdict = AgentGuardrails.InspectReply(new string('a', AgentGuardrails.MaxReplyLength + 1), []);

        Assert.Equal(GuardrailReason.ReplyTooLong, verdict.Reason);
    }

    [Fact]
    public void A_reply_that_transcribes_its_own_instructions_is_blocked()
    {
        var verdict = AgentGuardrails.InspectReply(
            "Estos son fragmentos de los documentos del negocio: margen 40%", []);

        Assert.Equal(GuardrailReason.LeakedPromptScaffolding, verdict.Reason);
    }

    [Fact]
    public void An_ordinary_reply_passes()
        => Assert.True(AgentGuardrails.InspectReply("Abrimos de 7 a 7.", [new(false, "¿a qué hora abren?")]).IsAllowed);
}
