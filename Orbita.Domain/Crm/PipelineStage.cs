using Orbita.Domain.Common;

namespace Orbita.Domain.Crm;

/// <summary>
/// An ordered column on a <see cref="Pipeline"/>. Won and lost are mutually exclusive
/// marks so a deal that lands here can be classified without a second lookup.
/// </summary>
public sealed class PipelineStage : Entity
{
    public const int NameMaxLength = 80;

    private PipelineStage(
        Guid id,
        Guid tenantId,
        Guid pipelineId,
        string name,
        int sortOrder,
        bool isWon,
        bool isLost,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        PipelineId = pipelineId;
        Name = name;
        SortOrder = sortOrder;
        IsWon = isWon;
        IsLost = isLost;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    public Guid PipelineId { get; }

    public string Name { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsWon { get; private set; }

    public bool IsLost { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PipelineStage Create(
        Guid tenantId,
        Guid pipelineId,
        string name,
        int sortOrder,
        bool isWon,
        bool isLost,
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

        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "Sort order cannot be negative.");
        }

        EnsureExclusiveOutcome(isWon, isLost);

        return new PipelineStage(
            Guid.NewGuid(),
            tenantId,
            pipelineId,
            RequireName(name),
            sortOrder,
            isWon,
            isLost,
            now,
            now);
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = RequireName(name);
        UpdatedAt = now;
    }

    public void SetSortOrder(int sortOrder, DateTimeOffset now)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "Sort order cannot be negative.");
        }

        SortOrder = sortOrder;
        UpdatedAt = now;
    }

    public void SetOutcome(bool isWon, bool isLost, DateTimeOffset now)
    {
        EnsureExclusiveOutcome(isWon, isLost);
        IsWon = isWon;
        IsLost = isLost;
        UpdatedAt = now;
    }

    private static void EnsureExclusiveOutcome(bool isWon, bool isLost)
    {
        if (isWon && isLost)
        {
            throw new ArgumentException("A stage cannot be both won and lost.");
        }
    }

    private static string RequireName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > NameMaxLength)
        {
            throw new ArgumentException(
                $"name must be between 1 and {NameMaxLength} characters.",
                nameof(name));
        }

        return trimmed;
    }
}
