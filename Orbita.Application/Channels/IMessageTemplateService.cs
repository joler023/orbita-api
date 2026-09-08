using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

public interface IMessageTemplateService
{
    Task<IReadOnlyList<MessageTemplateSummary>> ListAsync(Guid tenantId, Guid callerUserId, TemplateStatus? status, CancellationToken cancellationToken);

    /// <exception cref="TemplateAlreadyExistsException"/>
    Task<MessageTemplateSummary> CreateAsync(Guid tenantId, Guid callerUserId, CreateTemplateRequest request, CancellationToken cancellationToken);

    /// <returns>How many templates were synced.</returns>
    Task<int> SyncFromMetaAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken);
}
