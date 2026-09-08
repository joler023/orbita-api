using System.Text.RegularExpressions;
using Orbita.Domain.Common;

namespace Orbita.Domain.Crm;

/// <summary>
/// A person the tenant talks to (ORB-D02). Conversation history is owned by Track B;
/// this aggregate holds identity, custom fields, and the link to opportunities.
/// </summary>
public sealed class Contact : Entity
{
    public const int DisplayNameMaxLength = 200;
    public const int PhoneMaxLength = 32;
    public const int InstagramMaxLength = 64;
    public const int EmailMaxLength = 254;
    public const int ChannelMaxLength = 32;

    private Contact(
        Guid id,
        Guid tenantId,
        string displayName,
        string? phone,
        string? instagramUsername,
        string? email,
        string channel,
        Dictionary<string, string> customFields,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        DisplayName = displayName;
        Phone = phone;
        InstagramUsername = instagramUsername;
        Email = email;
        Channel = channel;
        CustomFields = customFields;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    public string DisplayName { get; private set; }

    public string? Phone { get; private set; }

    public string? InstagramUsername { get; private set; }

    public string? Email { get; private set; }

    public string Channel { get; private set; }

    public Dictionary<string, string> CustomFields { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Contact Create(
        Guid tenantId,
        string displayName,
        DateTimeOffset now,
        string? phone = null,
        string? instagramUsername = null,
        string? email = null,
        string? channel = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new Contact(
            Guid.NewGuid(),
            tenantId,
            RequireName(displayName),
            NormalizePhone(phone),
            NormalizeInstagram(instagramUsername),
            NormalizeEmail(email),
            NormalizeChannel(channel),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            now,
            now);
    }

    public void UpdateIdentity(
        string displayName,
        string? phone,
        string? instagramUsername,
        string? email,
        string? channel,
        DateTimeOffset now)
    {
        DisplayName = RequireName(displayName);
        Phone = NormalizePhone(phone);
        InstagramUsername = NormalizeInstagram(instagramUsername);
        Email = NormalizeEmail(email);
        Channel = NormalizeChannel(channel);
        UpdatedAt = now;
    }

    public void SetCustomField(string key, string? value, DateTimeOffset now)
    {
        var normalizedKey = RequireKey(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            CustomFields.Remove(normalizedKey);
        }
        else
        {
            CustomFields[normalizedKey] = value.Trim();
        }

        UpdatedAt = now;
    }

    public void ReplaceCustomFields(IReadOnlyDictionary<string, string> fields, DateTimeOffset now)
    {
        CustomFields.Clear();
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                CustomFields[RequireKey(key)] = value.Trim();
            }
        }

        UpdatedAt = now;
    }

    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = Regex.Replace(phone, @"[^\d+]", string.Empty);
        return digits.Length == 0 ? null : digits[..Math.Min(digits.Length, PhoneMaxLength)];
    }

    public static string? NormalizeInstagram(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var trimmed = username.Trim().TrimStart('@').ToLowerInvariant();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, InstagramMaxLength)];
    }

    private static string RequireName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > DisplayNameMaxLength)
        {
            throw new ArgumentException(
                $"displayName must be between 1 and {DisplayNameMaxLength} characters.",
                nameof(name));
        }

        return trimmed;
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim().ToLowerInvariant();
    }

    private static string NormalizeChannel(string? channel)
    {
        var trimmed = string.IsNullOrWhiteSpace(channel) ? "whatsapp" : channel.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "whatsapp" or "instagram" or "unknown" => trimmed,
            _ => throw new ArgumentException("Channel must be whatsapp, instagram, or unknown.", nameof(channel)),
        };
    }

    private static string RequireKey(string key)
    {
        var trimmed = key.Trim().ToLowerInvariant();
        if (trimmed.Length is < 1 or > 64 || !Regex.IsMatch(trimmed, "^[a-z][a-z0-9_]*$"))
        {
            throw new ArgumentException("Custom field key must be a lowercase identifier.", nameof(key));
        }

        return trimmed;
    }
}
