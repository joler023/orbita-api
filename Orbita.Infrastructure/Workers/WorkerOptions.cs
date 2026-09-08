namespace Orbita.Infrastructure.Workers;

/// <summary>
/// Cadence of the background workers, bound from <c>Channels:Workers</c>. Tests lower
/// these so a worker-driven assertion doesn't have to wait a real interval.
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Channels:Workers";

    /// <summary>How often <see cref="ChannelTokenExpiryWorker"/> sweeps for expired channel tokens.</summary>
    public int TokenExpiryIntervalMinutes { get; set; } = 60;
}
