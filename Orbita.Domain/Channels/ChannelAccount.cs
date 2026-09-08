using System.Security.Cryptography;
using System.Text;
using Orbita.Domain.Common;

namespace Orbita.Domain.Channels;

/// <summary>
/// A messaging account a tenant connected to the platform (ORB-B01): one WhatsApp
/// Business phone number, one Instagram business account, etc. Deliberately NOT
/// tenant query-filtered/RLS'd, the same exception already made for Subscription and
/// the Identity tokens: an inbound Meta webhook arrives carrying only
/// <see cref="ExternalId"/> (phone_number_id / ig_business_account_id) and no ambient
/// tenant, and Postgres RLS returns zero rows whenever no tenant is set in the session
/// — the unique (kind, external_id) index is precisely what lets the webhook resolve
/// the tenant (orbita-schema.dbml, channel_accounts). <see cref="TenantId"/> is still
/// re-checked by application code wherever it matters.
///
/// <para>The access token itself never lives here: <see cref="CredentialsRef"/> is an
/// opaque reference into IChannelCredentialStore (a Secrets Manager ARN in production,
/// a local Data Protection-encrypted row until then).</para>
/// </summary>
public sealed class ChannelAccount : Entity
{
    public const int ExternalIdMaxLength = 120;
    public const int WabaIdMaxLength = 120;
    public const int DisplayNameMaxLength = 200;
    public const int PhoneMaxLength = 20;

    /// <summary>How far ahead of <see cref="TokenExpiresAt"/> the dashboard should start warning.</summary>
    public static readonly TimeSpan ExpiryWarningWindow = TimeSpan.FromDays(7);

    private ChannelAccount(
        Guid id,
        Guid tenantId,
        ChannelKind kind,
        string externalId,
        string? wabaId,
        string displayName,
        string? phoneE164,
        string credentialsRef,
        string webhookSecret,
        ChannelStatus status,
        DateTimeOffset? tokenExpiresAt,
        DateTimeOffset? connectedAt,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        Kind = kind;
        ExternalId = externalId;
        WabaId = wabaId;
        DisplayName = displayName;
        PhoneE164 = phoneE164;
        CredentialsRef = credentialsRef;
        WebhookSecret = webhookSecret;
        Status = status;
        TokenExpiresAt = tokenExpiresAt;
        ConnectedAt = connectedAt;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public ChannelKind Kind { get; }

    /// <summary>Meta's id for the account: phone_number_id for WhatsApp, the IG business account id for Instagram.</summary>
    public string ExternalId { get; }

    /// <summary>
    /// The WhatsApp Business Account that owns the phone number. Not in orbita-schema.dbml
    /// — added because webhook subscription and template sync are WABA-level Graph API
    /// calls, not phone-number-level ones. Null for non-WhatsApp kinds.
    /// </summary>
    public string? WabaId { get; }

    public string DisplayName { get; private set; }

    /// <summary>WhatsApp only.</summary>
    public string? PhoneE164 { get; }

    public string CredentialsRef { get; private set; }

    /// <summary>
    /// Per-account random secret used as the <c>hub.verify_token</c> Meta echoes back
    /// when verifying this account's webhook callback URL. Not the HMAC key for
    /// X-Hub-Signature-256 — Meta signs payloads with the app secret, which is
    /// platform-wide configuration, not per-account state.
    /// </summary>
    public string WebhookSecret { get; }

    public ChannelStatus Status { get; private set; }

    public DateTimeOffset? TokenExpiresAt { get; private set; }

    public DateTimeOffset? ConnectedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static ChannelAccount ConnectWhatsApp(
        Guid tenantId,
        string phoneNumberId,
        string wabaId,
        string displayName,
        string? phoneE164,
        string credentialsRef,
        DateTimeOffset? tokenExpiresAt,
        DateTimeOffset now)
    {
        RequireTenant(tenantId);

        return new ChannelAccount(
            Guid.NewGuid(),
            tenantId,
            ChannelKind.WhatsApp,
            RequireLength(phoneNumberId, ExternalIdMaxLength, nameof(phoneNumberId)),
            RequireLength(wabaId, WabaIdMaxLength, nameof(wabaId)),
            RequireLength(displayName, DisplayNameMaxLength, nameof(displayName)),
            NormalizeOptional(phoneE164, PhoneMaxLength, nameof(phoneE164)),
            RequireNonEmpty(credentialsRef, nameof(credentialsRef)),
            GenerateWebhookSecret(),
            ChannelStatus.PendingVerification,
            tokenExpiresAt,
            connectedAt: null,
            now);
    }

    /// <summary>Meta verified our callback URL for this account — it is live from now on.</summary>
    public void MarkConnected(DateTimeOffset now)
    {
        Status = ChannelStatus.Connected;
        ConnectedAt ??= now;
    }

    public void MarkTokenExpired()
    {
        if (Status == ChannelStatus.Disconnected)
        {
            return;
        }

        Status = ChannelStatus.TokenExpired;
    }

    /// <summary>
    /// Stops the account without touching any conversation history (ORB-B01: "se puede
    /// desconectar la cuenta sin perder el historial"). The credential itself is deleted
    /// by the application service through the credential store; this only records the
    /// state change.
    /// </summary>
    public void Disconnect()
    {
        Status = ChannelStatus.Disconnected;
    }

    /// <summary>Reconnecting an existing account: swap in the new token and go back to verification.</summary>
    public void RotateCredentials(string credentialsRef, DateTimeOffset? tokenExpiresAt, string displayName)
    {
        CredentialsRef = RequireNonEmpty(credentialsRef, nameof(credentialsRef));
        TokenExpiresAt = tokenExpiresAt;
        DisplayName = RequireLength(displayName, DisplayNameMaxLength, nameof(displayName));
        Status = ChannelStatus.PendingVerification;
    }

    /// <summary>Constant-time comparison so the verification endpoint cannot leak the secret byte by byte.</summary>
    public bool VerifyWebhookToken(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(WebhookSecret);
        var actual = Encoding.UTF8.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public bool TokenExpiresSoon(DateTimeOffset now)
        => TokenExpiresAt is { } expiresAt && expiresAt <= now + ExpiryWarningWindow;

    public bool IsTokenExpired(DateTimeOffset now)
        => TokenExpiresAt is { } expiresAt && expiresAt <= now;

    private static string GenerateWebhookSecret()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }
    }

    private static string RequireNonEmpty(string value, string paramName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return trimmed;
    }

    private static string RequireLength(string value, int maxLength, string paramName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} must be between 1 and {maxLength} characters.", paramName);
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string paramName)
        => string.IsNullOrWhiteSpace(value) ? null : RequireLength(value, maxLength, paramName);
}
