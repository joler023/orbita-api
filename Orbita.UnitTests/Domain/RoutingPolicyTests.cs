using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Domain.Channels;

namespace Orbita.UnitTests.Domain;

/// <summary>ORB-C08. Order is the whole contract: first match wins, and a live conversation is never rerouted.</summary>
public sealed class RoutingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void The_first_matching_rule_wins_in_position_order()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var rules = new[]
        {
            RoutingRule.Create(Tenant, 1, "General", null, null, second, Now),
            RoutingRule.Create(Tenant, 0, "Instagram", ChannelKind.Instagram, null, first, Now),
        };

        Assert.Equal(first, RoutingPolicy.Decide(rules, null, ChannelKind.Instagram, "hola").AgentId);
        Assert.Equal(second, RoutingPolicy.Decide(rules, null, ChannelKind.WhatsApp, "hola").AgentId);
    }

    [Fact]
    public void Keywords_match_whole_words_only()
    {
        var rules = new[] { RoutingRule.Create(Tenant, 0, "Citas", null, "cita", Guid.NewGuid(), Now) };

        Assert.Same(RoutingDecision.NoRule, RoutingPolicy.Decide(rules, null, ChannelKind.WhatsApp, "felicitaciones"));
        Assert.NotNull(RoutingPolicy.Decide(rules, null, ChannelKind.WhatsApp, "quiero una cita").AgentId);
    }

    [Fact]
    public void A_conversation_that_already_has_an_assistant_keeps_it()
    {
        var assigned = Guid.NewGuid();
        var rules = new[] { RoutingRule.Create(Tenant, 0, "Todo al equipo", null, null, agentId: null, Now) };

        var decision = RoutingPolicy.Decide(rules, assigned, ChannelKind.WhatsApp, "hola");

        Assert.Equal(assigned, decision.AgentId);
        Assert.False(decision.LeaveForTeam);
    }

    [Fact]
    public void With_no_matching_rule_the_router_defers_to_the_default_assistant()
        => Assert.Same(RoutingDecision.NoRule, RoutingPolicy.Decide([], null, ChannelKind.WhatsApp, "hola"));

    [Fact]
    public void Business_hours_are_evaluated_in_the_tenants_time_zone()
    {
        // 09:00–18:00 Monday in Bogotá (UTC-5). 13:00 UTC Monday is 08:00 in Bogotá: closed.
        var hours = BusinessHours.Create(
            [new BusinessHoursSlot(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(18, 0))],
            OutsideHoursBehavior.LeaveForTeam);
        var bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
        var monday0800Bogota = new DateTimeOffset(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
        var monday1000Bogota = new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero);

        Assert.False(hours.IsOpenAt(monday0800Bogota, bogota));
        Assert.True(hours.IsOpenAt(monday1000Bogota, bogota));
    }

    [Fact]
    public void A_slot_that_closes_before_it_opens_is_refused()
        => Assert.Throws<ArgumentException>(() => BusinessHours.Create(
            [new BusinessHoursSlot(DayOfWeek.Friday, new TimeOnly(22, 0), new TimeOnly(6, 0))],
            OutsideHoursBehavior.AssistantAnswers));
}
