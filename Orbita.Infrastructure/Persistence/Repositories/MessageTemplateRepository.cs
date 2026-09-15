using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Channels;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class MessageTemplateRepository(OrbitaDbContext dbContext) : IMessageTemplateRepository
{
    public Task<MessageTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.MessageTemplates.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MessageTemplate>> ListByTenantAsync(Guid tenantId, TemplateStatus? status, CancellationToken cancellationToken)
    {
        var query = dbContext.MessageTemplates.Where(t => t.TenantId == tenantId);
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        return await query.OrderBy(t => t.MetaTemplateName).ToListAsync(cancellationToken);
    }

    public Task<MessageTemplate?> FindByNameAsync(Guid channelAccountId, string metaTemplateName, string language, CancellationToken cancellationToken)
        => dbContext.MessageTemplates.SingleOrDefaultAsync(
            t => t.ChannelAccountId == channelAccountId && t.MetaTemplateName == metaTemplateName && t.Language == language,
            cancellationToken);

    public async Task AddAsync(MessageTemplate template, CancellationToken cancellationToken)
        => await dbContext.MessageTemplates.AddAsync(template, cancellationToken);
}
