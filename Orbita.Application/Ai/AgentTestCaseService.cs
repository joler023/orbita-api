using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C11's saved test cases: the half of "se pueden guardar casos de prueba y volver a
/// ejecutarlos tras un cambio" that needed somewhere to live.
///
/// There is no "run" method here on purpose. Re-running a case is posting its turns to the
/// test bench that already exists — adding a second entry point would be a second place
/// where the prompt gets built, and the whole reason the test bench shares
/// <c>AgentPromptBuilder</c> with ORB-C04 is that a bench which predicts something other
/// than the product is worse than no bench.
/// </summary>
public interface IAgentTestCaseService
{
    /// <exception cref="AiAgentNotFoundException"/>
    Task<IReadOnlyList<AgentTestCaseDto>> ListAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken);

    /// <exception cref="AiAgentNotFoundException"/>
    /// <exception cref="ArgumentException">No name, no turns, or past a limit.</exception>
    /// <exception cref="TooManyTestCasesException">This assistant already has the maximum.</exception>
    Task<AgentTestCaseDto> SaveAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, string name, IReadOnlyList<AgentTestTurn> turns, CancellationToken cancellationToken);

    /// <summary>Deleting one that is not there is not an error — the owner wanted it gone.</summary>
    Task DeleteAsync(Guid tenantId, Guid callerUserId, Guid agentId, Guid testCaseId, CancellationToken cancellationToken);
}

public sealed record AgentTestCaseDto(Guid Id, string Name, IReadOnlyList<AgentTestTurn> Messages, DateTimeOffset CreatedAt)
{
    public static AgentTestCaseDto From(AgentTestCase testCase)
        => new(testCase.Id, testCase.Name, testCase.Turns, testCase.CreatedAt);
}

public sealed class TooManyTestCasesException()
    : Exception($"Este asistente ya tiene {AgentTestCase.MaxPerAgent} casos de prueba guardados. Borra alguno para guardar otro.");

public sealed class AgentTestCaseService(
    IAgentTestCaseRepository testCases,
    IAiAgentRepository agents,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAgentTestCaseService
{
    public async Task<IReadOnlyList<AgentTestCaseDto>> ListAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        return await unitOfWork.QueryInTenantScopeAsync(
            async ct =>
            {
                await RequireAgentAsync(tenantId, agentId, ct);

                return (IReadOnlyList<AgentTestCaseDto>)[.. (await testCases.ListByAgentAsync(tenantId, agentId, ct)).Select(AgentTestCaseDto.From)];
            },
            cancellationToken);
    }

    public async Task<AgentTestCaseDto> SaveAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, string name, IReadOnlyList<AgentTestTurn> turns, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var created = AgentTestCase.Create(tenantId, agentId, name, turns, timeProvider.GetUtcNow());

        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                await RequireAgentAsync(tenantId, agentId, ct);

                // Counted inside the same transaction as the insert: reading the count in
                // its own scope would let two saves both see nineteen.
                if (await testCases.CountByAgentAsync(tenantId, agentId, ct) >= AgentTestCase.MaxPerAgent)
                {
                    throw new TooManyTestCasesException();
                }

                testCases.Add(created);
            },
            cancellationToken);

        return AgentTestCaseDto.From(created);
    }

    public async Task DeleteAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, Guid testCaseId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                var testCase = await testCases.GetByIdAsync(tenantId, testCaseId, ct);

                // The agent id in the route has to match the one on the row: otherwise a
                // case could be deleted through any assistant's URL, which would make the
                // route a lie even though the tenant check held.
                if (testCase is not null && testCase.AgentId == agentId)
                {
                    testCases.Remove(testCase);
                }
            },
            cancellationToken);
    }

    private async Task RequireAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => _ = await agents.GetByIdAsync(tenantId, agentId, cancellationToken) ?? throw new AiAgentNotFoundException();
}
