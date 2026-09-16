using Moq;
using Orbita.Application.Ai;
using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C11's saved cases. What matters here is what the service refuses: a library that
/// grows without limit, and a case reachable through an assistant it does not belong to.
/// </summary>
public sealed class AgentTestCaseServiceTests
{
    private readonly Mock<IAgentTestCaseRepository> _testCases = new();
    private readonly Mock<IAiAgentRepository> _agents = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly PassThroughUnitOfWork _unitOfWork = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();
    private readonly AiAgent _agent;
    private readonly AgentTestCaseService _sut;

    private static readonly AgentTestTurn[] Exchange =
    [
        new(AgentTestRole.User, "¿hacen domicilios?"),
        new(AgentTestRole.Assistant, "Sí, en Laureles y Belén."),
    ];

    public AgentTestCaseServiceTests()
    {
        _agent = AiAgent.Create(_tenantId, "Espiga", "Cercano.", "Ayuda.", AgentStyle.Default, _time.GetUtcNow());
        _agents.Setup(r => r.GetByIdAsync(_tenantId, _agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_agent);

        _sut = new AgentTestCaseService(
            _testCases.Object, _agents.Object, _authorization.Object, _unitOfWork, _time);
    }

    [Fact]
    public async Task A_saved_case_keeps_the_exchange_in_the_shape_the_bench_takes()
    {
        AgentTestCase? stored = null;
        _testCases.Setup(r => r.Add(It.IsAny<AgentTestCase>())).Callback((AgentTestCase c) => stored = c);

        var saved = await _sut.SaveAsync(_tenantId, _callerId, _agent.Id, "  Domicilios  ", Exchange, CancellationToken.None);

        // One shape for the saved case and for POST .../test-chat, so re-running a case is
        // reading it and posting it — no translation, no second way to build the prompt.
        Assert.Equal(Exchange, saved.Messages);
        Assert.Equal("Domicilios", saved.Name);
        Assert.Equal(_agent.Id, stored!.AgentId);
        Assert.Equal(_tenantId, stored.TenantId);
    }

    [Fact]
    public async Task Saving_past_the_limit_is_refused()
    {
        _testCases
            .Setup(r => r.CountByAgentAsync(_tenantId, _agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentTestCase.MaxPerAgent);

        await Assert.ThrowsAsync<TooManyTestCasesException>(
            () => _sut.SaveAsync(_tenantId, _callerId, _agent.Id, "Uno más", Exchange, CancellationToken.None));

        _testCases.Verify(r => r.Add(It.IsAny<AgentTestCase>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_case_needs_a_name_somebody_will_recognise_later(string name)
        => await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SaveAsync(_tenantId, _callerId, _agent.Id, name, Exchange, CancellationToken.None));

    [Fact]
    public async Task A_case_with_no_messages_proves_nothing_and_is_refused()
        => await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SaveAsync(_tenantId, _callerId, _agent.Id, "Vacío", [], CancellationToken.None));

    [Fact]
    public async Task Saving_against_an_assistant_that_is_not_this_tenants_is_refused()
        => await Assert.ThrowsAsync<AiAgentNotFoundException>(
            () => _sut.SaveAsync(_tenantId, _callerId, Guid.NewGuid(), "Ajeno", Exchange, CancellationToken.None));

    [Fact]
    public async Task A_case_is_not_deletable_through_another_assistants_route()
    {
        var testCase = AgentTestCase.Create(_tenantId, _agent.Id, "Domicilios", Exchange, _time.GetUtcNow());
        _testCases.Setup(r => r.GetByIdAsync(_tenantId, testCase.Id, It.IsAny<CancellationToken>())).ReturnsAsync(testCase);

        await _sut.DeleteAsync(_tenantId, _callerId, Guid.NewGuid(), testCase.Id, CancellationToken.None);

        _testCases.Verify(r => r.Remove(It.IsAny<AgentTestCase>()), Times.Never);

        // Positive control: through its own assistant it does get deleted.
        await _sut.DeleteAsync(_tenantId, _callerId, _agent.Id, testCase.Id, CancellationToken.None);
        _testCases.Verify(r => r.Remove(testCase), Times.Once);
    }

    [Fact]
    public async Task Deleting_something_that_is_already_gone_is_not_an_error()
    {
        _testCases
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentTestCase?)null);

        await _sut.DeleteAsync(_tenantId, _callerId, _agent.Id, Guid.NewGuid(), CancellationToken.None);

        _testCases.Verify(r => r.Remove(It.IsAny<AgentTestCase>()), Times.Never);
    }
}
