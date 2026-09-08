using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Channels;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class ChannelAccountRepository(OrbitaDbContext dbContext) : IChannelAccountRepository
{
    public Task<ChannelAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.ChannelAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<ChannelAccount?> FindByKindAndExternalIdAsync(ChannelKind kind, string externalId, CancellationToken cancellationToken)
        => dbContext.ChannelAccounts.SingleOrDefaultAsync(a => a.Kind == kind && a.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<ChannelAccount>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.ChannelAccounts
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChannelAccount>> ListExpiringBeforeAsync(DateTimeOffset instant, CancellationToken cancellationToken)
        => await dbContext.ChannelAccounts
            .Where(a => a.Status == ChannelStatus.Connected && a.TokenExpiresAt != null && a.TokenExpiresAt <= instant)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ChannelAccount account, CancellationToken cancellationToken)
        => await dbContext.ChannelAccounts.AddAsync(account, cancellationToken);
}
