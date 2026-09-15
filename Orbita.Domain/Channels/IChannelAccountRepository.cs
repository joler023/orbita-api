namespace Orbita.Domain.Channels;

/// <summary>
/// channel_accounts is not RLS'd/query-filtered (see <see cref="ChannelAccount"/>), so
/// every tenant-facing method here filters by tenantId explicitly instead of relying
/// on the ambient tenant.
/// </summary>
public interface IChannelAccountRepository
{
    Task<ChannelAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The webhook's way in: resolves the owning tenant from Meta's own account id.</summary>
    Task<ChannelAccount?> FindByKindAndExternalIdAsync(ChannelKind kind, string externalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChannelAccount>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Connected accounts whose token expires at or before the given instant — the expiry sweep's input.</summary>
    Task<IReadOnlyList<ChannelAccount>> ListExpiringBeforeAsync(DateTimeOffset instant, CancellationToken cancellationToken);

    Task AddAsync(ChannelAccount account, CancellationToken cancellationToken);
}
