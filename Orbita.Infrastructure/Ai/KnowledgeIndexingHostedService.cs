using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orbita.Application.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Runs the knowledge indexer on a loop inside the API process.
///
/// ORB-C02 asks for embeddings to be generated in the background. The backlog assumes an
/// <c>Orbita.Workers</c> process fed by SQS (ORB-A02), which does not exist in this
/// solution — so this is the documented stand-in, in the same family as logging emails
/// instead of sending them. Its limits are real and worth knowing before production:
/// work stops when the API restarts (documents simply stay <c>Pending</c> and get picked
/// up next boot, which is why the queue is a database query rather than in-memory state),
/// and two API instances would both index the same document.
///
/// A scope is created per pass because the indexer depends on scoped services — the
/// DbContext and the unit of work — and a hosted service is a singleton.
/// </summary>
public sealed class KnowledgeIndexingHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<KnowledgeIndexingHostedService> logger) : BackgroundService
{
    /// <summary>
    /// How long to wait after a pass that found nothing. Short enough that an upload
    /// feels responsive, long enough that an idle instance is not hammering Postgres.
    /// </summary>
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    /// <summary>After a pass that did work, come straight back — there may be more queued.</summary>
    public static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Back off after an unexpected failure so a persistent fault does not spin the CPU.</summary>
    public static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Knowledge indexer started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var indexer = scope.ServiceProvider.GetRequiredService<IKnowledgeIndexer>();

                var indexed = await indexer.IndexPendingAsync(stoppingToken);
                delay = indexed > 0 ? BusyDelay : IdleDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The loop must survive anything: a crash here would silently stop every
                // tenant's indexing until someone restarted the API.
                logger.LogError(exception, "Knowledge indexing pass failed; retrying after a delay.");
                delay = ErrorDelay;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Knowledge indexer stopped.");
    }
}
