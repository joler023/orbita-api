using Orbita.Domain.Common;

namespace Orbita.Domain.Crm;

/// <summary>
/// A tenant-owned sales board (ORB-D04). A tenant can have several; exactly one is
/// the default so a new organization never opens an empty pipeline screen.
/// </summary>
public sealed class Pipeline : Entity
{
    public const int NameMaxLength = 80;

    private Pipeline(
        Guid id,
        Guid tenantId,
        string name,
        bool isDefault,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        IsDefault = isDefault;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    public string Name { get; private set; }

    public bool IsDefault { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Pipeline Create(Guid tenantId, string name, bool isDefault, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new Pipeline(
            Guid.NewGuid(),
            tenantId,
            RequireName(name),
            isDefault,
            now,
            now);
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = RequireName(name);
        UpdatedAt = now;
    }

    public void MarkDefault(DateTimeOffset now)
    {
        IsDefault = true;
        UpdatedAt = now;
    }

    public void ClearDefault(DateTimeOffset now)
    {
        IsDefault = false;
        UpdatedAt = now;
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
