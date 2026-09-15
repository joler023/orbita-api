namespace Orbita.Application.Crm;

public sealed record OpportunitySummary(
    Guid Id,
    Guid PipelineId,
    Guid StageId,
    string Title,
    decimal? Amount,
    Guid? AssignedToUserId,
    string? AssignedToName,
    Guid? ContactId,
    Guid? LastMoveEventId,
    DateTimeOffset CreatedAt);

public sealed record BoardStageSummary(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsWon,
    bool IsLost,
    decimal AmountSum,
    IReadOnlyList<OpportunitySummary> Opportunities);

public sealed record PipelineBoard(
    Guid PipelineId,
    string PipelineName,
    IReadOnlyList<BoardStageSummary> Stages);

public sealed record OpportunityBoardQuery(
    Guid? AssignedToUserId,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo);

public sealed record CreateOpportunityRequest(
    string Title,
    decimal? Amount,
    Guid? StageId,
    Guid? AssignedToUserId,
    Guid? ContactId = null);

public sealed record UpdateOpportunityRequest(
    string? Title,
    decimal? Amount,
    Guid? AssignedToUserId,
    bool ClearAssignee = false,
    Guid? ContactId = null,
    bool ClearContact = false);

public sealed record MoveOpportunityRequest(Guid StageId, Guid EventId);
