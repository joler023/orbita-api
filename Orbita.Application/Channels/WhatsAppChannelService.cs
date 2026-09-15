using Microsoft.Extensions.Logging;
using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Channels;

public sealed class WhatsAppChannelService(
    IChannelAccountRepository channelAccountRepository,
    IMetaAuthClient metaAuthClient,
    IWhatsAppCloudApiClient whatsAppClient,
    IChannelCredentialStore credentialStore,
    IChannelWebhookSettings webhookSettings,
    ITenantAuthorizationService authorizationService,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<WhatsAppChannelService> logger) : IWhatsAppChannelService
{
    public async Task<ChannelAccountDto> ConnectAsync(Guid tenantId, Guid callerUserId, ConnectWhatsAppRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageChannels, cancellationToken);

        var existing = await channelAccountRepository.FindByKindAndExternalIdAsync(ChannelKind.WhatsApp, request.PhoneNumberId, cancellationToken);
        if (existing is not null && existing.TenantId != tenantId)
        {
            throw new ChannelAlreadyConnectedException();
        }

        var (token, phoneNumber) = await ExchangeAndInspectAsync(request, cancellationToken);
        var credentialsRef = await credentialStore.StoreAsync(token.AccessToken, cancellationToken);
        var now = timeProvider.GetUtcNow();

        ChannelAccount account;
        if (existing is null)
        {
            account = ChannelAccount.ConnectWhatsApp(
                tenantId,
                request.PhoneNumberId,
                request.WabaId,
                phoneNumber.VerifiedName,
                NormalizePhone(phoneNumber.DisplayPhoneNumber),
                credentialsRef,
                token.ExpiresAt,
                now);
            await channelAccountRepository.AddAsync(account, cancellationToken);
        }
        else
        {
            // Reconnecting the same number: the old token is replaced, not kept around.
            var previousRef = existing.CredentialsRef;
            existing.RotateCredentials(credentialsRef, token.ExpiresAt, phoneNumber.VerifiedName);
            await credentialStore.DeleteAsync(previousRef, cancellationToken);
            account = existing;
        }

        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "channel.connected",
            nameof(ChannelAccount),
            account.Id,
            new { kind = ChannelKind.WhatsApp.ToString(), phoneNumberId = request.PhoneNumberId, wabaId = request.WabaId },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Only after the row is committed: Meta verifies the per-account callback URL
        // synchronously inside this call, and that GET is answered by looking the
        // account up by id. A subscription failure leaves the account pending — the
        // admin can retry through VerifyAsync — rather than failing the whole connect.
        await TrySubscribeWebhookAsync(account, token.AccessToken, cancellationToken);

        return ChannelAccountDto.From(account, now);
    }

    public async Task<IReadOnlyList<ChannelAccountDto>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewChannels, cancellationToken);

        var accounts = await channelAccountRepository.ListByTenantAsync(tenantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        return accounts.Select(a => ChannelAccountDto.From(a, now)).ToList();
    }

    public async Task<ChannelAccountDto> GetAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewChannels, cancellationToken);

        var account = await RequireAccountAsync(tenantId, channelAccountId, cancellationToken);
        return ChannelAccountDto.From(account, timeProvider.GetUtcNow());
    }

    public async Task<ChannelAccountDto> VerifyAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageChannels, cancellationToken);

        var account = await RequireAccountAsync(tenantId, channelAccountId, cancellationToken);
        var accessToken = await credentialStore.GetAsync(account.CredentialsRef, cancellationToken);

        try
        {
            await SubscribeWebhookAsync(account, accessToken, cancellationToken);
        }
        catch (MetaApiException exception)
        {
            throw new ChannelConnectionFailedException($"Meta rejected the webhook subscription: {exception.Message}");
        }

        // Meta's verification GET (handled by ChannelWebhookVerificationService in a
        // separate request) may already have flipped the row — re-read to report it.
        var refreshed = await RequireAccountAsync(tenantId, channelAccountId, cancellationToken);
        return ChannelAccountDto.From(refreshed, timeProvider.GetUtcNow());
    }

    public async Task DisconnectAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageChannels, cancellationToken);

        var account = await RequireAccountAsync(tenantId, channelAccountId, cancellationToken);
        account.Disconnect();
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "channel.disconnected",
            nameof(ChannelAccount),
            account.Id,
            new { kind = account.Kind.ToString(), externalId = account.ExternalId },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit, so a failure here can never leave a "disconnected" account
        // whose token is still usable in the next request that reads it.
        await credentialStore.DeleteAsync(account.CredentialsRef, cancellationToken);
    }

    private async Task<(MetaAccessToken Token, WhatsAppPhoneNumberInfo PhoneNumber)> ExchangeAndInspectAsync(
        ConnectWhatsAppRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await metaAuthClient.ExchangeCodeAsync(request.Code, cancellationToken);
            var expiresAt = token.ExpiresAt ?? await metaAuthClient.GetTokenExpiryAsync(token.AccessToken, cancellationToken);
            var phoneNumber = await whatsAppClient.GetPhoneNumberAsync(token.AccessToken, request.PhoneNumberId, cancellationToken);
            return (token with { ExpiresAt = expiresAt }, phoneNumber);
        }
        catch (MetaApiException exception)
        {
            throw new ChannelConnectionFailedException($"Meta rejected the connection: {exception.Message}");
        }
    }

    private async Task TrySubscribeWebhookAsync(ChannelAccount account, string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            await SubscribeWebhookAsync(account, accessToken, cancellationToken);
        }
        catch (MetaApiException exception)
        {
            logger.LogWarning(
                exception,
                "Webhook subscription for channel account {ChannelAccountId} failed; it stays pending verification.",
                account.Id);
        }
    }

    private Task SubscribeWebhookAsync(ChannelAccount account, string accessToken, CancellationToken cancellationToken)
    {
        var wabaId = account.WabaId ?? throw new InvalidOperationException("A WhatsApp account must have a WABA id.");
        var hasPublicUrl = webhookSettings.PublicBaseUrl.Length > 0;
        var callbackUrl = hasPublicUrl ? $"{webhookSettings.PublicBaseUrl}/api/webhooks/whatsapp/{account.Id:D}" : null;
        var verifyToken = hasPublicUrl ? account.WebhookSecret : null;
        return whatsAppClient.SubscribeWebhookAsync(accessToken, wabaId, callbackUrl, verifyToken, cancellationToken);
    }

    private async Task<ChannelAccount> RequireAccountAsync(Guid tenantId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        var account = await channelAccountRepository.GetByIdAsync(channelAccountId, cancellationToken);

        // channel_accounts is not RLS'd (see ChannelAccount) — the tenant check here is
        // the isolation, not a redundancy.
        if (account is null || account.TenantId != tenantId)
        {
            throw new ChannelAccountNotFoundException();
        }

        return account;
    }

    /// <summary>Meta formats display numbers with spaces/dashes ("+57 300 111 2233"); the DBML column is E.164.</summary>
    private static string? NormalizePhone(string displayPhoneNumber)
    {
        var digits = new string(displayPhoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : $"+{digits}";
    }
}
