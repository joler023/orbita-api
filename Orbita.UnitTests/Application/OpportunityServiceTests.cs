using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Crm;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class OpportunityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 15, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPipelineRepository> _pipelines = new();
    private readonly Mock<IPipelineStageRepository> _stages = new();
    private readonly Mock<IOpportunityRepository> _opportunities = new();
    private readonly Mock<IMembershipRepository> _memberships = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<ICrmRealtimePublisher> _realtime = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly OpportunityService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public OpportunityServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Pipeline?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Pipeline?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<PipelineStage?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<PipelineStage?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<PipelineStage>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<PipelineStage>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Opportunity?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Opportunity?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Opportunity>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Opportunity>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Membership?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Membership?>> query, CancellationToken ct) => query(ct));

        _sut = new OpportunityService(
            _pipelines.Object,
            _stages.Object,
            _opportunities.Object,
            _memberships.Object,
            _users.Object,
            _authorization.Object,
            _realtime.Object,
            _audit.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task GetBoardAsync_SumsAmountsPerStage()
    {
        var pipeline = Pipeline.Create(_tenantId, "Ventas", true, Now);
        var nuevo = PipelineStage.Create(_tenantId, pipeline.Id, "Nuevo", 0, false, false, Now);
        var ganada = PipelineStage.Create(_tenantId, pipeline.Id, "Ganada", 1, true, false, Now);
        var first = Opportunity.Create(_tenantId, pipeline.Id, nuevo.Id, "A", 100m, Now);
        var second = Opportunity.Create(_tenantId, pipeline.Id, nuevo.Id, "B", 50m, Now);

        _pipelines.Setup(r => r.GetByIdAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);
        _stages.Setup(r => r.GetByPipelineAsync(pipeline.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PipelineStage>)[nuevo, ganada]);
        _opportunities
            .Setup(r => r.GetByPipelineAsync(pipeline.Id, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Opportunity>)[first, second]);

        var board = await _sut.GetBoardAsync(
            _tenantId, _callerId, pipeline.Id, new OpportunityBoardQuery(null, null, null), CancellationToken.None);

        Assert.Equal(150m, board.Stages[0].AmountSum);
        Assert.Equal(0m, board.Stages[1].AmountSum);
        Assert.Equal(2, board.Stages[0].Opportunities.Count);
    }

    [Fact]
    public async Task MoveAsync_SameEventId_DoesNotSaveOrBroadcastAgain()
    {
        var pipeline = Pipeline.Create(_tenantId, "Ventas", true, Now);
        var from = PipelineStage.Create(_tenantId, pipeline.Id, "Nuevo", 0, false, false, Now);
        var to = PipelineStage.Create(_tenantId, pipeline.Id, "Propuesta", 1, false, false, Now);
        var deal = Opportunity.Create(_tenantId, pipeline.Id, from.Id, "Sitio", 10m, Now);
        var eventId = Guid.NewGuid();
        deal.MoveToStage(to.Id, eventId, Now);

        _opportunities.Setup(r => r.GetByIdAsync(deal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deal);
        _stages.Setup(r => r.GetByIdAsync(to.Id, It.IsAny<CancellationToken>())).ReturnsAsync(to);

        var result = await _sut.MoveAsync(
            _tenantId, _callerId, deal.Id, new MoveOpportunityRequest(to.Id, eventId), CancellationToken.None);

        Assert.Equal(to.Id, result.StageId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(
            r => r.PublishOpportunityChangedAsync(It.IsAny<Guid>(), It.IsAny<OpportunityChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MoveAsync_NewEventId_PersistsAndPublishes()
    {
        var pipeline = Pipeline.Create(_tenantId, "Ventas", true, Now);
        var from = PipelineStage.Create(_tenantId, pipeline.Id, "Nuevo", 0, false, false, Now);
        var to = PipelineStage.Create(_tenantId, pipeline.Id, "Propuesta", 1, false, false, Now);
        var deal = Opportunity.Create(_tenantId, pipeline.Id, from.Id, "Sitio", 10m, Now);
        var eventId = Guid.NewGuid();

        _opportunities.Setup(r => r.GetByIdAsync(deal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deal);
        _stages.Setup(r => r.GetByIdAsync(to.Id, It.IsAny<CancellationToken>())).ReturnsAsync(to);

        var result = await _sut.MoveAsync(
            _tenantId, _callerId, deal.Id, new MoveOpportunityRequest(to.Id, eventId), CancellationToken.None);

        Assert.Equal(to.Id, result.StageId);
        Assert.Equal(eventId, result.LastMoveEventId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(
            r => r.PublishOpportunityChangedAsync(
                _tenantId,
                It.Is<OpportunityChangedEvent>(e => e.Kind == OpportunityChangedKind.Moved && e.EventId == eventId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownAssignee_Throws()
    {
        var pipeline = Pipeline.Create(_tenantId, "Ventas", true, Now);
        var stage = PipelineStage.Create(_tenantId, pipeline.Id, "Nuevo", 0, false, false, Now);
        _pipelines.Setup(r => r.GetByIdAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);
        _stages.Setup(r => r.GetByPipelineAsync(pipeline.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PipelineStage>)[stage]);
        _memberships
            .Setup(r => r.GetByTenantAndUserAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Membership?)null);

        await Assert.ThrowsAsync<AssigneeNotInTenantException>(() =>
            _sut.CreateAsync(
                _tenantId,
                _callerId,
                pipeline.Id,
                new CreateOpportunityRequest("Sitio", 100m, null, Guid.NewGuid()),
                CancellationToken.None));
    }
}
