using System.Text.RegularExpressions;
using Orbita.Domain.Common;

namespace Orbita.Domain.Crm;

/// <summary>
/// A tenant-defined field on contacts that does not require a schema change (ORB-D02).
/// Values live in <see cref="Contact.CustomFields"/>.
/// </summary>
public sealed class ContactFieldDefinition : Entity
{
    public const int KeyMaxLength = 64;
    public const int LabelMaxLength = 80;

    private ContactFieldDefinition(Guid id, Guid tenantId, string key, string label, ContactFieldType fieldType, DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        Key = key;
        Label = label;
        FieldType = fieldType;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public string Key { get; }

    public string Label { get; private set; }

    public ContactFieldType FieldType { get; }

    public DateTimeOffset CreatedAt { get; }

    public static ContactFieldDefinition Create(Guid tenantId, string key, string label, ContactFieldType fieldType, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        var normalizedKey = key.Trim().ToLowerInvariant();
        if (normalizedKey.Length is < 1 or > KeyMaxLength || !Regex.IsMatch(normalizedKey, "^[a-z][a-z0-9_]*$"))
        {
            throw new ArgumentException("key must be a lowercase identifier.", nameof(key));
        }

        var trimmedLabel = label.Trim();
        if (trimmedLabel.Length is < 1 or > LabelMaxLength)
        {
            throw new ArgumentException($"label must be between 1 and {LabelMaxLength} characters.", nameof(label));
        }

        return new ContactFieldDefinition(Guid.NewGuid(), tenantId, normalizedKey, trimmedLabel, fieldType, now);
    }

    public void Rename(string label)
    {
        var trimmed = label.Trim();
        if (trimmed.Length is < 1 or > LabelMaxLength)
        {
            throw new ArgumentException($"label must be between 1 and {LabelMaxLength} characters.", nameof(label));
        }

        Label = trimmed;
    }
}
