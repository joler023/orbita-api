using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Domain;

public sealed class AiAgentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static AiAgent NewAgent(AgentTone tone = AgentTone.Balanced)
        => AiAgent.Create(
            Guid.NewGuid(),
            " Asistente ",
            " Eres amable y breve. ",
            " Nunca inventas precios. ",
            tone,
            Now);

    [Fact]
    public void A_new_agent_starts_disabled()
    {
        // Nobody wants an assistant answering customers the moment it is created.
        Assert.False(NewAgent().IsEnabled);
    }

    [Fact]
    public void What_the_owner_typed_is_kept_trimmed_and_separate()
    {
        var agent = NewAgent();

        Assert.Equal("Asistente", agent.Name);
        Assert.Equal("Eres amable y breve.", agent.Personality);
        Assert.Equal("Nunca inventas precios.", agent.Instructions);
    }

    [Fact]
    public void The_prompt_sent_to_the_model_is_composed_from_both_fields()
    {
        // The screen never edits this directly; it has to be assembled from what the
        // screen does collect.
        var agent = NewAgent();

        Assert.Contains("Asistente", agent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Eres amable y breve.", agent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Nunca inventas precios.", agent.SystemPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentTone.Formal)]
    [InlineData(AgentTone.Balanced)]
    [InlineData(AgentTone.Conversational)]
    public void Each_tone_maps_to_a_usable_temperature(AgentTone tone)
    {
        var agent = NewAgent(tone);

        Assert.InRange(agent.Temperature, 0m, 2m);
    }

    [Fact]
    public void A_more_conversational_tone_means_a_higher_temperature()
    {
        // The direction is the part that matters — the exact numbers are tunable without
        // a frontend release, which is the whole reason the tone is stored instead.
        Assert.True(NewAgent(AgentTone.Formal).Temperature < NewAgent(AgentTone.Balanced).Temperature);
        Assert.True(NewAgent(AgentTone.Balanced).Temperature < NewAgent(AgentTone.Conversational).Temperature);
    }

    [Fact]
    public void Reconfiguring_rewrites_both_the_prompt_and_the_temperature()
    {
        var agent = NewAgent(AgentTone.Formal);
        var originalTemperature = agent.Temperature;

        agent.Reconfigure("Eres entusiasta.", "Ofreces el catálogo completo.", AgentTone.Conversational);

        Assert.Equal(AgentTone.Conversational, agent.Tone);
        Assert.NotEqual(originalTemperature, agent.Temperature);
        Assert.Contains("Eres entusiasta.", agent.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Eres amable y breve.", agent.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Renaming_also_rewrites_the_prompt()
    {
        // The name is part of what the model is told it is called.
        var agent = NewAgent();

        agent.Rename("Sofía");

        Assert.Contains("Sofía", agent.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_name_personality_or_instructions_is_rejected()
    {
        var tenantId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => AiAgent.Create(tenantId, "  ", "p", "i", AgentTone.Balanced, Now));
        Assert.Throws<ArgumentException>(() => AiAgent.Create(tenantId, "n", "  ", "i", AgentTone.Balanced, Now));
        Assert.Throws<ArgumentException>(() => AiAgent.Create(tenantId, "n", "p", "  ", AgentTone.Balanced, Now));
    }

    [Fact]
    public void Text_longer_than_the_column_is_rejected_here_rather_than_at_the_database()
    {
        var tooLong = new string('a', AiAgent.InstructionsMaxLength + 1);

        var failure = Assert.Throws<ArgumentException>(
            () => AiAgent.Create(Guid.NewGuid(), "n", "p", tooLong, AgentTone.Balanced, Now));

        Assert.Contains(AiAgent.InstructionsMaxLength.ToString(), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_available_tool_can_be_enabled()
    {
        var agent = NewAgent();

        agent.EnableTools([AiToolCatalog.ConsultarConocimiento]);

        Assert.Equal([AiToolCatalog.ConsultarConocimiento], agent.Tools);
    }

    [Fact]
    public void A_tool_that_does_not_work_yet_cannot_be_enabled()
    {
        // Accepting it would leave an assistant that looks configured and silently never
        // does the thing.
        var agent = NewAgent();

        var failure = Assert.Throws<ArgumentException>(
            () => agent.EnableTools([AiToolCatalog.CrearOportunidad]));

        Assert.Contains("not available", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(agent.Tools);
    }

    [Fact]
    public void An_unknown_tool_is_rejected()
        => Assert.Throws<ArgumentException>(() => NewAgent().EnableTools(["hacer_cafe"]));

    [Fact]
    public void Enabling_tools_replaces_the_set_rather_than_adding_to_it()
    {
        // The screen edits checkboxes, so it always sends the whole state.
        var agent = NewAgent();
        agent.EnableTools([AiToolCatalog.ConsultarConocimiento]);

        agent.EnableTools([]);

        Assert.Empty(agent.Tools);
    }

    [Fact]
    public void The_same_tool_listed_twice_is_stored_once()
        => Assert.Single(EnabledTwice());

    [Fact]
    public void The_seeded_agent_is_disabled_and_can_already_read_its_documents()
    {
        var agent = AiAgent.CreateDefault(Guid.NewGuid(), "Panadería Aurora", Now);

        Assert.False(agent.IsEnabled);
        Assert.Contains("Panadería Aurora", agent.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal([AiToolCatalog.ConsultarConocimiento], agent.Tools);
    }

    [Fact]
    public void Turning_an_agent_on_and_off_is_its_own_operation()
    {
        var agent = NewAgent();

        agent.SetEnabled(true);
        Assert.True(agent.IsEnabled);

        agent.SetEnabled(false);
        Assert.False(agent.IsEnabled);
    }

    private static IReadOnlyList<string> EnabledTwice()
    {
        var agent = NewAgent();
        agent.EnableTools([AiToolCatalog.ConsultarConocimiento, AiToolCatalog.ConsultarConocimiento]);
        return agent.Tools;
    }
}

public sealed class AiToolCatalogTests
{
    [Fact]
    public void The_catalog_lists_every_tool_ORB_C05_specifies()
    {
        var keys = AiToolCatalog.All.Select(tool => tool.Key).ToList();

        Assert.Contains(AiToolCatalog.ConsultarConocimiento, keys);
        Assert.Contains(AiToolCatalog.CrearOportunidad, keys);
        Assert.Contains(AiToolCatalog.MoverEtapa, keys);
        Assert.Contains(AiToolCatalog.AgendarCita, keys);
        Assert.Contains(AiToolCatalog.EscalarAHumano, keys);
    }

    [Fact]
    public void Only_the_knowledge_lookup_works_today()
    {
        // The rest depend on modules that do not exist. Saying so is the point of the
        // endpoint — the alternative is a frontend that hardcodes the list and needs a
        // release every time one lands.
        Assert.True(AiToolCatalog.IsAvailable(AiToolCatalog.ConsultarConocimiento));
        Assert.False(AiToolCatalog.IsAvailable(AiToolCatalog.CrearOportunidad));
    }

    [Fact]
    public void Every_unavailable_tool_explains_why()
    {
        // So the UI can say when it is coming instead of just greying it out.
        foreach (var tool in AiToolCatalog.All.Where(tool => !tool.IsAvailable))
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.UnavailableReason), $"{tool.Key} has no reason.");
        }
    }

    [Fact]
    public void Nothing_in_the_catalog_speaks_in_jargon()
    {
        // Screen 2.6 is forbidden from showing model vocabulary, and it renders these
        // strings verbatim.
        string[] banned = ["prompt", "token", "temperatura", "temperature", "modelo", "embedding"];

        foreach (var tool in AiToolCatalog.All)
        {
            var text = $"{tool.DisplayName} {tool.Description} {tool.UnavailableReason}";

            foreach (var word in banned)
            {
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
