using System.Text;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;

namespace Orbita.Application.Crm;

/// <summary>
/// ORB-D13 draft: synchronous CSV download for contacts and opportunities.
/// Async job + signed R2 URL is the acceptance target; this ships the useful export now.
/// Conversation export waits on Track B.
/// </summary>
public sealed class ExportService(
    IContactRepository contactRepository,
    IOpportunityRepository opportunityRepository,
    IPipelineRepository pipelineRepository,
    IPipelineStageRepository stageRepository,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IExportService
{
    private const int ExportLimit = 10_000;
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public async Task<ExportFile> ExportContactsAsync(
        Guid tenantId,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewContacts, cancellationToken);
        var contacts = await unitOfWork.QueryInTenantScopeAsync(
            ct => contactRepository.ListForExportAsync(tenantId, ExportLimit, ct),
            cancellationToken);

        var csv = CsvExport.Build(
            ["id", "displayName", "phone", "instagramUsername", "email", "channel", "createdAt", "updatedAt"],
            contacts.Select(contact => new string?[]
            {
                contact.Id.ToString(),
                contact.DisplayName,
                contact.Phone,
                contact.InstagramUsername,
                contact.Email,
                contact.Channel,
                CsvExport.FormatInstant(contact.CreatedAt),
                CsvExport.FormatInstant(contact.UpdatedAt),
            }));

        return ToFile($"orbita-contacts-{Stamp()}.csv", csv);
    }

    public async Task<ExportFile> ExportOpportunitiesAsync(
        Guid tenantId,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewPipeline, cancellationToken);
        var opportunities = await unitOfWork.QueryInTenantScopeAsync(
            ct => opportunityRepository.ListForExportAsync(tenantId, ExportLimit, ct),
            cancellationToken);

        var pipelines = await unitOfWork.QueryInTenantScopeAsync(
            ct => pipelineRepository.GetByTenantAsync(tenantId, ct),
            cancellationToken);
        var pipelineNames = pipelines.ToDictionary(pipeline => pipeline.Id, pipeline => pipeline.Name);

        var stageNames = new Dictionary<Guid, string>();
        foreach (var pipeline in pipelines)
        {
            var stages = await unitOfWork.QueryInTenantScopeAsync(
                ct => stageRepository.GetByPipelineAsync(pipeline.Id, ct),
                cancellationToken);
            foreach (var stage in stages)
            {
                stageNames[stage.Id] = stage.Name;
            }
        }

        var csv = CsvExport.Build(
            ["id", "title", "amount", "pipeline", "stage", "contactId", "assignedToUserId", "createdAt", "updatedAt"],
            opportunities.Select(opportunity => new string?[]
            {
                opportunity.Id.ToString(),
                opportunity.Title,
                opportunity.Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pipelineNames.GetValueOrDefault(opportunity.PipelineId),
                stageNames.GetValueOrDefault(opportunity.StageId),
                opportunity.ContactId?.ToString(),
                opportunity.AssignedToUserId?.ToString(),
                CsvExport.FormatInstant(opportunity.CreatedAt),
                CsvExport.FormatInstant(opportunity.UpdatedAt),
            }));

        return ToFile($"orbita-opportunities-{Stamp()}.csv", csv);
    }

    private string Stamp() => timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss");

    private static ExportFile ToFile(string fileName, string csv)
        => new(fileName, "text/csv; charset=utf-8", Utf8Bom.GetBytes(csv));
}
