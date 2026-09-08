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
    public const int InstagramUserIdMaxLength = 120;

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
        DateTimeOffset updatedAt,
        string? instagramUserId = null,
        DateTimeOffset? lastSeenAt = null)
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
        InstagramUserId = instagramUserId;
        LastSeenAt = lastSeenAt;
    }

    public Guid TenantId { get; }

    public string DisplayName { get; private set; }

    public string? Phone { get; private set; }

    public string? InstagramUsername { get; private set; }

    /// <summary>Instagram's own business-scoped user id (ORB-B03) — distinct from <see cref="InstagramUsername"/>, which can change.</summary>
    public string? InstagramUserId { get; private set; }

    public string? Email { get; private set; }

    public string Channel { get; private set; }

    public Dictionary<string, string> CustomFields { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Last time an inbound message from this contact was processed (ORB-B03).</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

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

    /// <summary>
    /// A contact created from an inbound channel message (ORB-B03), not from the
    /// dashboard — <paramref name="externalUserId"/> is the channel's own identifier
    /// (WhatsApp's wa_id, already digits-only; Instagram's IG-scoped user id), and
    /// becomes <see cref="Phone"/> or <see cref="InstagramUserId"/> respectively.
    /// </summary>
    public static Contact CreateFromChannel(
        Guid tenantId,
        string channel,
        string externalUserId,
        string? displayName,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            throw new ArgumentException("External user id is required.", nameof(externalUserId));
        }

        var normalizedChannel = NormalizeChannel(channel);
        var name = string.IsNullOrWhiteSpace(displayName) ? externalUserId : displayName;

        return normalizedChannel switch
        {
            "whatsapp" => new Contact(
                Guid.NewGuid(), tenantId, RequireName(name), NormalizePhone(externalUserId), null, null,
                normalizedChannel, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), now, now,
                lastSeenAt: now),
            "instagram" => new Contact(
                Guid.NewGuid(), tenantId, RequireName(name), null, null, null,
                normalizedChannel, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), now, now,
                instagramUserId: NormalizeInstagramUserId(externalUserId), lastSeenAt: now),
            _ => throw new ArgumentException("Channel must be whatsapp or instagram.", nameof(channel)),
        };
    }

    /// <summary>Bumps <see cref="LastSeenAt"/> on every inbound message (ORB-B03) — never touched by dashboard edits.</summary>
    public void RecordSeen(DateTimeOffset now) => LastSeenAt = now;

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

        // Digits only, no leading '+' (ORB-B03): Meta's wa_id arrives without one, and
        // keeping it here would duplicate every WhatsApp contact on first inbound message.
        var digits = Regex.Replace(phone, @"[^\d]", string.Empty);
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

    private static string NormalizeInstagramUserId(string value)
    {
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, InstagramUserIdMaxLength)];
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
