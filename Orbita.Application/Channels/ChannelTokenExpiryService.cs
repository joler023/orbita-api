using Orbita.Domain.Channels;
using Orbita.Domain.Common;

namespace Orbita.Application.Channels;

public sealed class ChannelTokenExpiryService(
    IChannelAccountRepository channelAccountRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IChannelTokenExpiryService
{
    public async Task<int> MarkExpiredAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiring = await channelAccountRepository.ListExpiringBeforeAsync(now, cancellationToken);
        if (expiring.Count == 0)
        {
            return 0;
        }

        foreach (var account in expiring)
        {
            account.MarkTokenExpired();
        }

        // channel_accounts is not RLS'd, so this cross-tenant save needs no ambient
        // tenant — and no audit entry is written here because audit_log *is* RLS'd and
        // a single sweep spans many tenants. The status change is visible in the DTO.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return expiring.Count;
    }
}
