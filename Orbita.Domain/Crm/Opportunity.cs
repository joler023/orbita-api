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
        Guid? assignedToUserId,
        Guid? contactId,
        Guid? lastMoveEventId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        PipelineId = pipelineId;
        StageId = stageId;
        Title = title;
        Amount = amount;
        AssignedToUserId = assignedToUserId;
        ContactId = contactId;
        LastMoveEventId = lastMoveEventId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    public Guid PipelineId { get; }

    public Guid StageId { get; private set; }

    public string Title { get; private set; }

    public decimal? Amount { get; private set; }

    public Guid? AssignedToUserId { get; private set; }

    public Guid? ContactId { get; private set; }

    /// <summary>
    /// Last client-generated move id applied to this card (ORB-D05). Repeating the
    /// same id is a no-op so a SignalR echo of the mover's own action cannot bounce
    /// the card.
    /// </summary>
    public Guid? LastMoveEventId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Opportunity Create(
        Guid tenantId,
        Guid pipelineId,
        Guid stageId,
        string title,
        decimal? amount,
        DateTimeOffset now,
        Guid? assignedToUserId = null,
        Guid? contactId = null)
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
            assignedToUserId,
            contactId,
            lastMoveEventId: null,
            now,
            now);
    }

    /// <returns>False when <paramref name="eventId"/> was already applied.</returns>
    public bool MoveToStage(Guid stageId, Guid eventId, DateTimeOffset now)
    {
        if (stageId == Guid.Empty)
        {
            throw new ArgumentException("Stage id is required.", nameof(stageId));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Move event id is required.", nameof(eventId));
        }

        if (LastMoveEventId == eventId)
        {
            return false;
        }

        StageId = stageId;
        LastMoveEventId = eventId;
        UpdatedAt = now;
        return true;
    }

    public void Rename(string title, DateTimeOffset now)
    {
        Title = RequireTitle(title);
        UpdatedAt = now;
    }

    public void SetAmount(decimal? amount, DateTimeOffset now)
    {
        if (amount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        Amount = amount;
        UpdatedAt = now;
    }

    public void AssignTo(Guid? userId, DateTimeOffset now)
    {
        AssignedToUserId = userId;
        UpdatedAt = now;
    }

    public void LinkContact(Guid? contactId, DateTimeOffset now)
    {
        ContactId = contactId;
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
