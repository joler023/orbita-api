using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Channels;

public sealed class MessageTemplateService(
    IMessageTemplateRepository templates,
    IChannelAccountRepository channelAccounts,
    IChannelCredentialStore credentialStore,
    IWhatsAppCloudApiClient whatsAppClient,
    ITenantAuthorizationService authorizationService,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IMessageTemplateService
{
    private const string DefaultLanguage = "es";

    public async Task<IReadOnlyList<MessageTemplateSummary>> ListAsync(Guid tenantId, Guid callerUserId, TemplateStatus? status, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewChannels, cancellationToken);

        // message_templates is RLS'd, and `app.tenant_id` only exists inside a transaction
        // that sets it — a plain read here answers zero rows, always. See CLAUDE.md,
        // "Reads of RLS'd tables must run inside a tenant scope".
        var list = await unitOfWork.QueryInTenantScopeAsync(
            ct => templates.ListByTenantAsync(tenantId, status, ct),
            cancellationToken);

        return list.Select(ToSummary).ToList();
    }

    public async Task<MessageTemplateSummary> CreateAsync(Guid tenantId, Guid callerUserId, CreateTemplateRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageTemplates, cancellationToken);

        var account = await RequireAccountAsync(tenantId, request.ChannelAccountId, cancellationToken);
        var language = string.IsNullOrWhiteSpace(request.Language) ? DefaultLanguage : request.Language;
        var now = timeProvider.GetUtcNow();
        MessageTemplate? template = null;

        // The duplicate check and the insert share one transaction: outside a tenant
        // scope the lookup finds nothing even when the template exists, and the "already
        // exists" conflict would never be raised.
        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                if (await templates.FindByNameAsync(account.Id, request.MetaTemplateName, language, ct) is not null)
                {
                    throw new TemplateAlreadyExistsException();
                }

                template = MessageTemplate.Create(tenantId, account.Id, request.MetaTemplateName, request.Category, request.Language, request.Body, now);
                await templates.AddAsync(template, ct);

                await auditLogger.RecordAsync(
                    tenantId, callerUserId, "template.created", nameof(MessageTemplate), template.Id,
                    new { metaTemplateName = template.MetaTemplateName, language = template.Language },
                    ct);
            },
            cancellationToken);

        return ToSummary(template!);
    }

    public async Task<int> SyncFromMetaAsync(Guid tenantId, Guid callerUserId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageTemplates, cancellationToken);

        var account = await RequireAccountAsync(tenantId, channelAccountId, cancellationToken);
        var wabaId = account.WabaId ?? throw new InvalidOperationException("A WhatsApp account must have a WABA id.");
        var accessToken = await credentialStore.GetAsync(account.CredentialsRef, cancellationToken);
        var remoteTemplates = await whatsAppClient.ListTemplatesAsync(accessToken, wabaId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var syncedCount = 0;

        // One scope for the whole sync: each lookup decides whether a template is created
        // or updated, so a read that cannot see existing rows would duplicate every one of
        // them on every sync.
        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                foreach (var remote in remoteTemplates)
                {
                    var local = await templates.FindByNameAsync(account.Id, remote.Name, remote.Language, ct);
                    if (local is null)
                    {
                        // Body isn't part of Meta's template list response in a form we can
                        // render locally yet — placeholder until a template detail call is added.
                        local = MessageTemplate.Create(tenantId, account.Id, remote.Name, MessageCategory.Utility, remote.Language, "[sincronizado desde Meta]", now);
                        await templates.AddAsync(local, ct);
                    }

                    local.ApplyMetaStatus(MetaTemplateStatusMapper.Map(remote.Status), remote.RejectedReason, now);
                    syncedCount++;
                }

                await auditLogger.RecordAsync(
                    tenantId, callerUserId, "template.synced", nameof(MessageTemplate), null,
                    new { channelAccountId = account.Id, count = syncedCount },
                    ct);
            },
            cancellationToken);

        return syncedCount;
    }

    private async Task<ChannelAccount> RequireAccountAsync(Guid tenantId, Guid channelAccountId, CancellationToken cancellationToken)
    {
        var account = await channelAccounts.GetByIdAsync(channelAccountId, cancellationToken);
        if (account is null || account.TenantId != tenantId)
        {
            throw new ChannelAccountNotFoundException();
        }

        return account;
    }

    private static MessageTemplateSummary ToSummary(MessageTemplate template)
        => new(template.Id, template.ChannelAccountId, template.MetaTemplateName, template.Category, template.Language, template.Body, template.Status, template.RejectedReason, template.ApprovedAt);
}
