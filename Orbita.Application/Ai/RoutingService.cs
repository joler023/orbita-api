using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.Application.Ai;

/// <summary>ORB-C08: the router's configuration, and the decision it makes for a message.</summary>
public interface IRoutingService
{
    Task<IReadOnlyList<RoutingRuleDto>> ListRulesAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the ordered rule list. The order in <paramref name="rules"/> is the
    /// evaluation order — "visible y configurable" means exactly this list.
    /// </summary>
    /// <exception cref="ArgumentException">Too many rules, or a rule that is invalid.</exception>
    /// <exception cref="AiAgentNotFoundException">A rule points at an assistant that is not this tenant's.</exception>
    Task<IReadOnlyList<RoutingRuleDto>> ReplaceRulesAsync(
        Guid tenantId, Guid callerUserId, IReadOnlyList<RoutingRuleRequest> rules, CancellationToken cancellationToken);

    /// <exception cref="AiAgentNotFoundException"/>
    Task<AiAgentDto> SetBusinessHoursAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, BusinessHoursRequest? request, CancellationToken cancellationToken);
}

public sealed record RoutingRuleRequest(string Name, ChannelKind? Channel, string? Keyword, Guid? AgentId);

public sealed record RoutingRuleDto(Guid Id, int Position, string Name, ChannelKind? Channel, string? Keyword, Guid? AgentId);

public sealed record BusinessHoursRequest(IReadOnlyList<BusinessHoursSlot> Slots, OutsideHoursBehavior OutsideHours);

public sealed class RoutingService(
    IRoutingRuleRepository rules,
    IAiAgentRepository agents,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRoutingService
{
    public async Task<IReadOnlyList<RoutingRuleDto>> ListRulesAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var list = await unitOfWork.QueryInTenantScopeAsync(ct => rules.ListByTenantAsync(tenantId, ct), cancellationToken);

        return [.. list.Select(ToDto)];
    }

    public async Task<IReadOnlyList<RoutingRuleDto>> ReplaceRulesAsync(
        Guid tenantId, Guid callerUserId, IReadOnlyList<RoutingRuleRequest> requests, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count > RoutingRule.MaxRulesPerTenant)
        {
            throw new ArgumentException($"At most {RoutingRule.MaxRulesPerTenant} routing rules.", nameof(requests));
        }

        var now = timeProvider.GetUtcNow();
        var built = requests.Select((request, index) =>
            RoutingRule.Create(tenantId, index, request.Name, request.Channel, request.Keyword, request.AgentId, now)).ToList();

        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                // A rule that routes to another tenant's assistant would hand that tenant's
                // customer to a stranger's configuration. The list is read tenant-scoped, so
                // an id from elsewhere is simply absent.
                var ownAgents = (await agents.ListByTenantAsync(tenantId, ct)).Select(a => a.Id).ToHashSet();

                if (built.Any(rule => rule.AgentId is { } id && !ownAgents.Contains(id)))
                {
                    throw new AiAgentNotFoundException();
                }

                await rules.ReplaceAsync(tenantId, built, ct);
            },
            cancellationToken);

        return [.. built.Select(ToDto)];
    }

    public async Task<AiAgentDto> SetBusinessHoursAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, BusinessHoursRequest? request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var hours = request is null ? null : BusinessHours.Create(request.Slots, request.OutsideHours);
        AiAgent? agent = null;

        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                agent = await agents.GetByIdAsync(tenantId, agentId, ct) ?? throw new AiAgentNotFoundException();
                agent.SetBusinessHours(hours);
            },
            cancellationToken);

        return AiAgentDto.From(agent!);
    }

    private static RoutingRuleDto ToDto(RoutingRule rule)
        => new(rule.Id, rule.Position, rule.Name, rule.Channel, rule.Keyword, rule.AgentId);
}

/// <summary>
/// What the router decides for one inbound message. Pure: given the rules, the agent the
/// conversation already has, and the moment, it answers who handles it.
/// </summary>
public static class RoutingPolicy
{
    public static RoutingDecision Decide(
        IReadOnlyList<RoutingRule> rules,
        Guid? assignedAgentId,
        ChannelKind channel,
        string? message)
    {
        ArgumentNullException.ThrowIfNull(rules);

        // Sticky: a conversation keeps the assistant that took it (ORB-C04's invariant).
        // Rules pick who takes a *new* conversation; they do not bounce a live one between
        // assistants mid-exchange.
        if (assignedAgentId is { } assigned)
        {
            return RoutingDecision.Agent(assigned, matchedRuleId: null);
        }

        var match = rules.OrderBy(rule => rule.Position).FirstOrDefault(rule => rule.Matches(channel, message));

        return match switch
        {
            null => RoutingDecision.NoRule,
            { AgentId: { } agentId } => RoutingDecision.Agent(agentId, match.Id),
            _ => RoutingDecision.Team(match.Id),
        };
    }
}

public sealed record RoutingDecision(Guid? AgentId, bool LeaveForTeam, Guid? MatchedRuleId)
{
    /// <summary>No rule applied: fall back to the tenant's enabled assistant, as before routing existed.</summary>
    public static RoutingDecision NoRule { get; } = new(null, false, null);

    public static RoutingDecision Agent(Guid agentId, Guid? matchedRuleId) => new(agentId, false, matchedRuleId);

    public static RoutingDecision Team(Guid matchedRuleId) => new(null, true, matchedRuleId);
}
