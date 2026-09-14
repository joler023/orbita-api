using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Domain;

public sealed class AiAgentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static AiAgent NewAgent(AgentStyle? style = null)
        => AiAgent.Create(
            Guid.NewGuid(),
            " Asistente ",
            " Eres amable y breve. ",
            " Nunca inventas precios. ",
            style ?? AgentStyle.Default,
            Now);

    private static AgentStyle StyleWith(
        FormalityLevel formality = FormalityLevel.Balanced,
        VerbosityLevel verbosity = VerbosityLevel.Balanced,
        EnergyLevel energy = EnergyLevel.Balanced)
        => new(formality, verbosity, energy);

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

    [Fact]
    public void A_new_agent_sits_in_the_middle_of_every_slider()
    {
        var agent = NewAgent();

        Assert.Equal(FormalityLevel.Balanced, agent.Style.Formality);
        Assert.Equal(VerbosityLevel.Balanced, agent.Style.Verbosity);
        Assert.Equal(EnergyLevel.Balanced, agent.Style.Energy);
    }

    [Theory]
    [InlineData(FormalityLevel.Formal, VerbosityLevel.Brief, EnergyLevel.Neutral)]
    [InlineData(FormalityLevel.Balanced, VerbosityLevel.Balanced, EnergyLevel.Balanced)]
    [InlineData(FormalityLevel.Warm, VerbosityLevel.Detailed, EnergyLevel.Enthusiastic)]
    public void Every_combination_of_the_three_axes_maps_to_a_usable_temperature(
        FormalityLevel formality,
        VerbosityLevel verbosity,
        EnergyLevel energy)
    {
        var agent = NewAgent(StyleWith(formality, verbosity, energy));

        Assert.InRange(agent.Temperature, 0m, 2m);
    }

    [Fact]
    public void A_warmer_assistant_gets_a_higher_temperature()
    {
        // The direction is the part that matters — the exact numbers are tunable without
        // a frontend release, which is the whole reason the style is stored instead.
        Assert.True(
            NewAgent(StyleWith(formality: FormalityLevel.Formal)).Temperature
                < NewAgent(StyleWith(formality: FormalityLevel.Balanced)).Temperature);
        Assert.True(
            NewAgent(StyleWith(formality: FormalityLevel.Balanced)).Temperature
                < NewAgent(StyleWith(formality: FormalityLevel.Warm)).Temperature);
    }

    [Fact]
    public void A_more_enthusiastic_assistant_gets_a_higher_temperature()
    {
        Assert.True(
            NewAgent(StyleWith(energy: EnergyLevel.Neutral)).Temperature
                < NewAgent(StyleWith(energy: EnergyLevel.Balanced)).Temperature);
        Assert.True(
            NewAgent(StyleWith(energy: EnergyLevel.Balanced)).Temperature
                < NewAgent(StyleWith(energy: EnergyLevel.Enthusiastic)).Temperature);
    }

    [Fact]
    public void How_long_replies_are_does_not_change_how_varied_they_are()
    {
        // Length is a budget, not a sampling parameter. Moving the wrong knob is exactly
        // the confusion the three separate axes exist to avoid.
        var brief = NewAgent(StyleWith(verbosity: VerbosityLevel.Brief));
        var detailed = NewAgent(StyleWith(verbosity: VerbosityLevel.Detailed));

        Assert.Equal(brief.Temperature, detailed.Temperature);
    }

    [Fact]
    public void A_briefer_assistant_gets_a_smaller_token_budget()
    {
        var brief = NewAgent(StyleWith(verbosity: VerbosityLevel.Brief));
        var balanced = NewAgent(StyleWith(verbosity: VerbosityLevel.Balanced));
        var detailed = NewAgent(StyleWith(verbosity: VerbosityLevel.Detailed));

        Assert.True(brief.MaxTokens < balanced.MaxTokens);
        Assert.True(balanced.MaxTokens < detailed.MaxTokens);
    }

    [Fact]
    public void The_style_reaches_the_model_as_words_not_only_as_numbers()
    {
        // Temperature cannot express "brief" or "formal" — a hotter model is more varied,
        // not shorter or friendlier. Without the guidance in the prompt, two of the three
        // sliders would quietly do nothing.
        var formalAndBrief = NewAgent(StyleWith(FormalityLevel.Formal, VerbosityLevel.Brief));
        var warmAndDetailed = NewAgent(StyleWith(FormalityLevel.Warm, VerbosityLevel.Detailed));

        Assert.Contains("usted", formalAndBrief.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("una o dos frases", formalAndBrief.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cercan", warmAndDetailed.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ejemplos", warmAndDetailed.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Applying_a_configuration_rewrites_the_prompt_the_temperature_and_the_budget()
    {
        var agent = NewAgent(StyleWith(FormalityLevel.Formal, VerbosityLevel.Brief, EnergyLevel.Neutral));
        var originalTemperature = agent.Temperature;
        var originalMaxTokens = agent.MaxTokens;

        agent.ApplyConfiguration(
            "Sofía",
            "Eres entusiasta.",
            "Ofreces el catálogo completo.",
            StyleWith(FormalityLevel.Warm, VerbosityLevel.Detailed, EnergyLevel.Enthusiastic),
            [AiToolCatalog.ConsultarConocimiento]);

        Assert.Equal(FormalityLevel.Warm, agent.Style.Formality);
        Assert.NotEqual(originalTemperature, agent.Temperature);
        Assert.NotEqual(originalMaxTokens, agent.MaxTokens);
        // The name is part of what the model is told it is called.
        Assert.Contains("Sofía", agent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Eres entusiasta.", agent.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Eres amable y breve.", agent.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal([AiToolCatalog.ConsultarConocimiento], agent.Tools);
    }

    [Fact]
    public void Applying_a_configuration_leaves_the_agent_on_or_off_as_it_was()
    {
        // Publishing a configuration must not be what puts an assistant in front of
        // customers — that is SetEnabled's job and nothing else's.
        var agent = NewAgent();
        agent.SetEnabled(true);

        agent.ApplyConfiguration("n", "p", "i", AgentStyle.Default, []);

        Assert.True(agent.IsEnabled);
    }

    [Fact]
    public void An_empty_name_personality_or_instructions_is_rejected()
    {
        var tenantId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => AiAgent.Create(tenantId, "  ", "p", "i", AgentStyle.Default, Now));
        Assert.Throws<ArgumentException>(() => AiAgent.Create(tenantId, "n", "  ", "i", AgentStyle.Default, Now));
        Assert.Throws<ArgumentException>(() => AiAgent.Create(tenantId, "n", "p", "  ", AgentStyle.Default, Now));
    }

    [Fact]
    public void Text_longer_than_the_column_is_rejected_here_rather_than_at_the_database()
    {
        var tooLong = new string('a', AiAgent.InstructionsMaxLength + 1);

        var failure = Assert.Throws<ArgumentException>(
            () => AiAgent.Create(Guid.NewGuid(), "n", "p", tooLong, AgentStyle.Default, Now));

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
