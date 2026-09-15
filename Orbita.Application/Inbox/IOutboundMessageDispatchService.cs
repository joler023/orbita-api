using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

public interface IOutboundMessageDispatchService
{
    Task DispatchAsync(OutboundMessageJob job, CancellationToken cancellationToken);
}
