namespace Orbita.Application.Crm;

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

public interface IExportService
{
    Task<ExportFile> ExportContactsAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    Task<ExportFile> ExportOpportunitiesAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);
}
