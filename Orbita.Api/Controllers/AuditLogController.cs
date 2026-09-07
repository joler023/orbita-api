using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Audit;
using Orbita.Domain.Audit;

namespace Orbita.Api.Controllers;

/// <summary>ORB-A15: a tenant's own audit trail, consultable and filterable by an Owner/Admin.</summary>
[ApiController]
[Authorize]
public sealed class AuditLogController(IAuditLogQueryService auditLogQueryService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/audit-log")]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntryDto>>> Query(
        Guid tenantId,
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] Guid? actorId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = new AuditLogQuery(entityType, entityId, actorId, from, to, limit);
        var entries = await auditLogQueryService.QueryAsync(tenantId, User.GetUserId(), query, cancellationToken);
        return Ok(entries);
    }
}
