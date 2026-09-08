namespace Orbita.Domain.Channels;

public interface IMessageTemplateRepository
{
    Task<MessageTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageTemplate>> ListByTenantAsync(Guid tenantId, TemplateStatus? status, CancellationToken cancellationToken);

    /// <summary>Used both to detect a duplicate registration and by ORB-B07's Meta sync to find the local row a remote template maps to.</summary>
    Task<MessageTemplate?> FindByNameAsync(Guid channelAccountId, string metaTemplateName, string language, CancellationToken cancellationToken);

    Task AddAsync(MessageTemplate template, CancellationToken cancellationToken);
}
