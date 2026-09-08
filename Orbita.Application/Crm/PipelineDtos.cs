namespace Orbita.Application.Crm;

public sealed record PipelineSummary(
    Guid Id,
    string Name,
    bool IsDefault,
    IReadOnlyList<StageSummary> Stages);

public sealed record StageSummary(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsWon,
    bool IsLost);

public sealed record CreatePipelineRequest(string Name);

public sealed record UpdatePipelineRequest(string? Name, bool? IsDefault);

public sealed record CreateStageRequest(string Name, bool IsWon = false, bool IsLost = false);

public sealed record UpdateStageRequest(string? Name, bool? IsWon, bool? IsLost);

public sealed record ReorderStagesRequest(IReadOnlyList<Guid> StageIds);

public sealed record DeleteStageRequest(Guid? RelocateToStageId);
