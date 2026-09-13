using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Domain;

/// <summary>
/// The one rule all of these serve: an assistant that is answering customers must not
/// change because somebody is typing on the configuration screen.
/// </summary>
public sealed class AiAgentDraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static AiAgent LiveAgent()
    {
        var agent = AiAgent.Create(
            Guid.NewGuid(), "Asistente", "Eres amable.", "No inventas precios.", AgentStyle.Default, Now);
        agent.EnableTools([AiToolCatalog.ConsultarConocimiento]);
        agent.SetEnabled(true);

        return agent;
    }

    private static AiAgentDraft DraftFor(AiAgent agent)
        => AiAgentDraft.For(
            agent,
            "Sofía",
            "Eres entusiasta.",
            "Ofreces el catálogo completo.",
            new AgentStyle(FormalityLevel.Warm, VerbosityLevel.Detailed, EnergyLevel.Enthusiastic),
            [],
            Now);

    [Fact]
    public void A_draft_belongs_to_its_agent_and_its_tenant()
    {
        var agent = LiveAgent();

        var draft = DraftFor(agent);

        Assert.Equal(agent.Id, draft.AgentId);
        Assert.Equal(agent.TenantId, draft.TenantId);
    }

    [Fact]
    public void Writing_a_draft_leaves_the_live_assistant_untouched()
    {
        var agent = LiveAgent();

        DraftFor(agent);

        Assert.Equal("Asistente", agent.Name);
        Assert.Equal("Eres amable.", agent.Personality);
        Assert.Equal(AgentStyle.Default, agent.Style);
        Assert.Contains("Eres amable.", agent.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void What_was_typed_is_kept_trimmed()
    {
        var draft = AiAgentDraft.For(LiveAgent(), "  Sofía  ", "  p  ", "  i  ", AgentStyle.Default, [], Now);

        Assert.Equal("Sofía", draft.Name);
        Assert.Equal("p", draft.Personality);
        Assert.Equal("i", draft.Instructions);
    }

    [Fact]
    public void Saving_again_replaces_the_pending_draft_rather_than_stacking_another()
    {
        var draft = DraftFor(LiveAgent());
        var later = Now.AddMinutes(5);

        draft.Replace("Otra", "p2", "i2", AgentStyle.Default, [AiToolCatalog.ConsultarConocimiento], later);

        Assert.Equal("Otra", draft.Name);
        Assert.Equal(AgentStyle.Default, draft.Style);
        Assert.Equal(later, draft.UpdatedAt);
        Assert.Equal([AiToolCatalog.ConsultarConocimiento], draft.Tools);
    }

    [Fact]
    public void A_draft_identical_to_the_live_assistant_is_not_a_pending_change()
    {
        // Otherwise opening the screen and pressing "Guardar" lights the badge forever,
        // and an owner who learns to ignore it will ignore a real pending change too.
        var agent = LiveAgent();

        var draft = AiAgentDraft.For(
            agent, agent.Name, agent.Personality, agent.Instructions, agent.Style, agent.Tools, Now);

        Assert.False(draft.DiffersFrom(agent));
    }

    [Fact]
    public void Whitespace_alone_is_not_a_pending_change()
    {
        var agent = LiveAgent();

        var draft = AiAgentDraft.For(
            agent, $"  {agent.Name}  ", agent.Personality, agent.Instructions, agent.Style, agent.Tools, Now);

        Assert.False(draft.DiffersFrom(agent));
    }

    [Theory]
    [InlineData(FormalityLevel.Warm, VerbosityLevel.Balanced, EnergyLevel.Balanced)]
    [InlineData(FormalityLevel.Balanced, VerbosityLevel.Brief, EnergyLevel.Balanced)]
    [InlineData(FormalityLevel.Balanced, VerbosityLevel.Balanced, EnergyLevel.Enthusiastic)]
    public void Moving_any_one_slider_counts_as_a_pending_change(
        FormalityLevel formality,
        VerbosityLevel verbosity,
        EnergyLevel energy)
    {
        var agent = LiveAgent();

        var draft = AiAgentDraft.For(
            agent,
            agent.Name,
            agent.Personality,
            agent.Instructions,
            new AgentStyle(formality, verbosity, energy),
            agent.Tools,
            Now);

        Assert.True(draft.DiffersFrom(agent));
    }

    [Fact]
    public void Changing_the_enabled_tools_counts_as_a_pending_change()
    {
        var agent = LiveAgent();

        var draft = AiAgentDraft.For(
            agent, agent.Name, agent.Personality, agent.Instructions, agent.Style, [], Now);

        Assert.True(draft.DiffersFrom(agent));
    }

    [Fact]
    public void The_order_the_checkboxes_came_back_in_is_not_a_change()
    {
        var agent = LiveAgent();

        var draft = AiAgentDraft.For(
            agent,
            agent.Name,
            agent.Personality,
            agent.Instructions,
            agent.Style,
            [.. agent.Tools.Reverse()],
            Now);

        Assert.False(draft.DiffersFrom(agent));
    }

    [Fact]
    public void Publishing_moves_every_edited_field_onto_the_live_assistant()
    {
        var agent = LiveAgent();
        var draft = DraftFor(agent);

        agent.ApplyConfiguration(draft.Name, draft.Personality, draft.Instructions, draft.Style, draft.Tools);

        Assert.Equal("Sofía", agent.Name);
        Assert.Equal("Eres entusiasta.", agent.Personality);
        Assert.Equal("Ofreces el catálogo completo.", agent.Instructions);
        Assert.Equal(draft.Style, agent.Style);
        Assert.Empty(agent.Tools);
        // And once published there is nothing pending left to publish.
        Assert.False(draft.DiffersFrom(agent));
    }
}
