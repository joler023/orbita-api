using System.Security.Cryptography;
using System.Text;
using Orbita.Application.Audit;
using Orbita.Domain.Audit;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;

namespace Orbita.Application.Channels;

public sealed class ChannelWebhookVerificationService(
    IChannelAccountRepository channelAccountRepository,
    IChannelWebhookSettings webhookSettings,
    ITenantContextSetter tenantContextSetter,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IChannelWebhookVerificationService
{
    private const string SubscribeMode = "subscribe";

    public async Task<string> VerifyWhatsAppAsync(Guid? channelAccountId, string? mode, string? verifyToken, string? challenge, CancellationToken cancellationToken)
    {
        if (mode != SubscribeMode || string.IsNullOrEmpty(challenge))
        {
            throw new WebhookVerificationFailedException();
        }

        if (channelAccountId is null)
        {
            if (!FixedTimeEquals(webhookSettings.GlobalVerifyToken, verifyToken))
            {
                throw new WebhookVerificationFailedException();
            }

            return challenge;
        }

        var account = await channelAccountRepository.GetByIdAsync(channelAccountId.Value, cancellationToken);
        if (account is null || !account.VerifyWebhookToken(verifyToken))
        {
            throw new WebhookVerificationFailedException();
        }

        if (account.Status == ChannelStatus.PendingVerification)
        {
            // This request is anonymous (Meta is the caller), so the tenant is taken from
            // the account itself — needed for the RLS'd audit_log insert to land.
            tenantContextSetter.SetTenant(account.TenantId);
            account.MarkConnected(timeProvider.GetUtcNow());
            await auditLogger.RecordSystemActionAsync(
                account.TenantId,
                AuditActorType.System,
                "channel.webhook_verified",
                nameof(ChannelAccount),
                account.Id,
                new { kind = account.Kind.ToString(), externalId = account.ExternalId },
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return challenge;
    }

    private static bool FixedTimeEquals(string expected, string? candidate)
    {
        if (expected.Length == 0 || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(candidate));
    }
}
