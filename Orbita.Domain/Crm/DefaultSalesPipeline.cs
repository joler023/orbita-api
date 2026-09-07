namespace Orbita.Domain.Crm;

/// <summary>
/// The board every new organization receives so the pipeline screen is never empty
/// (ORB-D04). Spanish names match the rest of the product UI.
/// </summary>
public static class DefaultSalesPipeline
{
    public const string PipelineName = "Ventas";

    public static readonly IReadOnlyList<(string Name, bool IsWon, bool IsLost)> Stages =
    [
        ("Nuevo", false, false),
        ("En conversación", false, false),
        ("Propuesta", false, false),
        ("Ganada", true, false),
        ("Perdida", false, true),
    ];

    public static (Pipeline Pipeline, IReadOnlyList<PipelineStage> Stages) Create(Guid tenantId, DateTimeOffset now)
    {
        var pipeline = Pipeline.Create(tenantId, PipelineName, isDefault: true, now);
        var stages = Stages
            .Select((stage, index) => PipelineStage.Create(
                tenantId,
                pipeline.Id,
                stage.Name,
                index,
                stage.IsWon,
                stage.IsLost,
                now))
            .ToArray();

        return (pipeline, stages);
    }
}
