namespace Orbita.Application.Outbox;

public interface IOutboxDispatchService
{
    /// <summary>Claims and publishes one batch of due events; a publish failure registers on that event and moves on, it never stops the batch.</summary>
    Task DispatchOnceAsync(int batchSize, CancellationToken cancellationToken);
}
