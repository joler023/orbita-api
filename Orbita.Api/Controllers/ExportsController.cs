using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Crm;

namespace Orbita.Api.Controllers;

/// <summary>ORB-D13: sync CSV downloads (async R2 job is a follow-up).</summary>
[ApiController]
[Authorize]
public sealed class ExportsController(IExportService exportService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/exports/contacts")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportContacts(Guid tenantId, CancellationToken cancellationToken)
    {
        var file = await exportService.ExportContactsAsync(tenantId, User.GetUserId(), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("api/tenants/{tenantId:guid}/exports/opportunities")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportOpportunities(Guid tenantId, CancellationToken cancellationToken)
    {
        var file = await exportService.ExportOpportunitiesAsync(tenantId, User.GetUserId(), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
