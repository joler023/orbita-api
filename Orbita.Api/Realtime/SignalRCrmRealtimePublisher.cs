using Microsoft.AspNetCore.SignalR;
using Orbita.Application.Crm;

namespace Orbita.Api.Realtime;

public sealed class SignalRCrmRealtimePublisher(IHubContext<CrmHub> hubContext) : ICrmRealtimePublisher
{
    public const string OpportunityChangedMethod = "opportunityChanged";

    public Task PublishOpportunityChangedAsync(
        Guid tenantId,
        OpportunityChangedEvent change,
        CancellationToken cancellationToken)
        => hubContext.Clients
            .Group(CrmHub.TenantGroup(tenantId))
            .SendAsync(OpportunityChangedMethod, change, cancellationToken);
}
