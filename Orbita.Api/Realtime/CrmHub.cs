using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Orbita.Api.Identity;
using Orbita.Application.Identity;
using Orbita.Domain.Identity;

namespace Orbita.Api.Realtime;

/// <summary>Live kanban updates for a tenant (ORB-D05).</summary>
[Authorize]
public sealed class CrmHub(ITenantAuthorizationService authorizationService) : Hub
{
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:D}";

    public async Task JoinTenant(Guid tenantId)
    {
        await authorizationService.EnsurePermissionAsync(
            tenantId,
            Context.User!.GetUserId(),
            Permission.ViewPipeline,
            Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
    }
}
