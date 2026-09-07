using Orbita.Domain.Crm;

namespace Orbita.UnitTests.Domain;

public sealed class PipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_TrimsNameAndMarksDefault()
    {
        var pipeline = Pipeline.Create(TenantId, "  Ventas  ", isDefault: true, Now);

        Assert.Equal("Ventas", pipeline.Name);
        Assert.True(pipeline.IsDefault);
        Assert.Equal(TenantId, pipeline.TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyName(string name)
    {
        Assert.Throws<ArgumentException>(() => Pipeline.Create(TenantId, name, isDefault: true, Now));
    }

    [Fact]
    public void Stage_CannotBeBothWonAndLost()
    {
        var pipelineId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            PipelineStage.Create(TenantId, pipelineId, "Cerrado", 0, isWon: true, isLost: true, Now));
    }

    [Fact]
    public void DefaultSalesPipeline_SeedsWonAndLostColumns()
    {
        var (pipeline, stages) = DefaultSalesPipeline.Create(TenantId, Now);

        Assert.Equal("Ventas", pipeline.Name);
        Assert.True(pipeline.IsDefault);
        Assert.Equal(5, stages.Count);
        Assert.Contains(stages, s => s is { Name: "Ganada", IsWon: true, IsLost: false });
        Assert.Contains(stages, s => s is { Name: "Perdida", IsWon: false, IsLost: true });
        Assert.Equal(stages.Select((_, i) => i), stages.Select(s => s.SortOrder));
    }

    [Fact]
    public void Opportunity_MoveToStage_UpdatesStage()
    {
        var pipelineId = Guid.NewGuid();
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var deal = Opportunity.Create(TenantId, pipelineId, from, "Sitio web", 1_000m, Now);

        deal.MoveToStage(to, Now.AddMinutes(1));

        Assert.Equal(to, deal.StageId);
    }
}
