using Orbita.Domain.Common;

namespace Orbita.Domain.Crm;

/// <summary>
/// A deal sitting on a pipeline stage. ORB-D04 only needs the stage link so deleting
/// a column can relocate its cards; the kanban itself is ORB-D05.
/// </summary>
public sealed class Opportunity : Entity
{
    public const int TitleMaxLength = 200;

    private Opportunity(
        Guid id,
        Guid tenantId,
        Guid pipelineId,
        Guid stageId,
        string title,
        decimal? amount,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        PipelineId = pipelineId;
        StageId = stageId;
        Title = title;
        Amount = amount;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    public Guid PipelineId { get; }

    public Guid StageId { get; private set; }

    public string Title { get; private set; }

    public decimal? Amount { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Opportunity Create(
        Guid tenantId,
        Guid pipelineId,
        Guid stageId,
        string title,
        decimal? amount,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (pipelineId == Guid.Empty)
        {
            throw new ArgumentException("Pipeline id is required.", nameof(pipelineId));
        }

        if (stageId == Guid.Empty)
        {
            throw new ArgumentException("Stage id is required.", nameof(stageId));
        }

        if (amount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        return new Opportunity(
            Guid.NewGuid(),
            tenantId,
            pipelineId,
            stageId,
            RequireTitle(title),
            amount,
            now,
            now);
    }

    public void MoveToStage(Guid stageId, DateTimeOffset now)
    {
        if (stageId == Guid.Empty)
        {
            throw new ArgumentException("Stage id is required.", nameof(stageId));
        }

        StageId = stageId;
        UpdatedAt = now;
    }

    private static string RequireTitle(string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length is < 1 or > TitleMaxLength)
        {
            throw new ArgumentException(
                $"title must be between 1 and {TitleMaxLength} characters.",
                nameof(title));
        }

        return trimmed;
    }
}
