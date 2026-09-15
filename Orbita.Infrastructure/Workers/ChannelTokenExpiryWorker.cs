using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orbita.Application.Channels;

namespace Orbita.Infrastructure.Workers;

/// <summary>
/// Hourly sweep (configurable) that marks channel accounts whose token has expired
/// (ORB-B01). Each tick runs in its own DI scope so it gets a fresh DbContext, the
/// same way a request would.
/// </summary>
public sealed class ChannelTokenExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<ChannelTokenExpiryWorker> logger)
    : PollingWorker(TimeSpan.FromMinutes(Math.Max(1, options.Value.TokenExpiryIntervalMinutes)), logger)
{
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var expiryService = scope.ServiceProvider.GetRequiredService<IChannelTokenExpiryService>();

        var expired = await expiryService.MarkExpiredAsync(cancellationToken);
        if (expired > 0)
        {
            logger.LogWarning("{Count} channel account token(s) expired; the owning organizations need to reconnect them.", expired);
        }
    }
}
