using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Orbita.Infrastructure.Workers;

/// <summary>
/// Base for every "wake up every N, do one unit of work" background service in this
/// codebase (ORB-B01 onward). One tick's failure is logged and the loop keeps going —
/// a transient database error must never silently kill a worker for the rest of the
/// process's life. Host shutdown cancels the timer and ends the loop cleanly.
/// </summary>
public abstract class PollingWorker(TimeSpan interval, ILogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "{Worker} tick failed; will retry on the next interval.", GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);
}
