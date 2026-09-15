using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Crm;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class PipelineServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPipelineRepository> _pipelines = new();
    private readonly Mock<IPipelineStageRepository> _stages = new();
    private readonly Mock<IOpportunityRepository> _opportunities = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly PipelineService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public PipelineServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Pipeline?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Pipeline?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Pipeline>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Pipeline>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<PipelineStage?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<PipelineStage?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<PipelineStage>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<PipelineStage>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Opportunity>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Opportunity>>> query, CancellationToken ct) => query(ct));

        _sut = new PipelineService(
            _pipelines.Object,
            _stages.Object,
            _opportunities.Object,
            _authorization.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task DeleteAsync_WhenOnlyPipelineRemains_Throws()
    {
        var pipeline = Pipeline.Create(_tenantId, "Ventas", isDefault: true, Now);
        _pipelines.Setup(r => r.GetByIdAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);
        _pipelines.Setup(r => r.CountByTenantAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Assert.ThrowsAsync<CannotDeleteLastPipelineException>(
            () => _sut.DeleteAsync(_tenantId, _callerId, pipeline.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteStageAsync_WithoutRelocateWhenStageHasDeals_Throws()
    {
        var pipeline = Pipeline.Create(_tenantId, "Ventas", isDefault: true, Now);
        var stage = PipelineStage.Create(_tenantId, pipeline.Id, "Nuevo", 0, false, false, Now);
        _pipelines.Setup(r => r.GetByIdAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);
        _stages.Setup(r => r.GetByIdAsync(stage.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stage);
        _stages.Setup(r => r.CountByPipelineAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _opportunities.Setup(r => r.CountByStageAsync(stage.Id, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        await Assert.ThrowsAsync<StageHasOpportunitiesException>(() =>
            _sut.DeleteStageAsync(_tenantId, _callerId, pipeline.Id, stage.Id, new DeleteStageRequest(null), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteStageAsync_RelocatesOpportunitiesThenRemovesStage()
    {
        var pipeline = Pipeline.Create(_tenantId, "Ventas", isDefault: true, Now);
        var from = PipelineStage.Create(_tenantId, pipeline.Id, "Nuevo", 0, false, false, Now);
        var to = PipelineStage.Create(_tenantId, pipeline.Id, "Propuesta", 1, false, false, Now);
        var deal = Opportunity.Create(_tenantId, pipeline.Id, from.Id, "Sitio", 100m, Now);

        _pipelines.Setup(r => r.GetByIdAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);
        _stages.Setup(r => r.GetByIdAsync(from.Id, It.IsAny<CancellationToken>())).ReturnsAsync(from);
        _stages.Setup(r => r.GetByIdAsync(to.Id, It.IsAny<CancellationToken>())).ReturnsAsync(to);
        _stages.Setup(r => r.GetByPipelineAsync(pipeline.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PipelineStage>)[to]);
        _stages.Setup(r => r.CountByPipelineAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _opportunities.Setup(r => r.CountByStageAsync(from.Id, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _opportunities.Setup(r => r.GetByStageAsync(from.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Opportunity>)[deal]);

        var result = await _sut.DeleteStageAsync(
            _tenantId,
            _callerId,
            pipeline.Id,
            from.Id,
            new DeleteStageRequest(to.Id),
            CancellationToken.None);

        Assert.Equal(to.Id, deal.StageId);
        _stages.Verify(r => r.Remove(from), Times.Once);
        Assert.Equal("Ventas", result.Name);
        _authorization.Verify(
            a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ManagePipeline, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListAsync_WhenCallerLacksPermission_DoesNotQueryPipelines()
    {
        _authorization
            .Setup(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ViewPipeline, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("no"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.ListAsync(_tenantId, _callerId, CancellationToken.None));

        _pipelines.Verify(r => r.GetByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
