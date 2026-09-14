using Microsoft.Extensions.Caching.Memory;
using Orbita.Application.Ai;

namespace Orbita.Infrastructure.Ai;

public sealed class ModelPreferenceCacheInvalidator(IMemoryCache cache) : IModelPreferenceCacheInvalidator
{
    public void Invalidate(Guid tenantId) => TenantAwareLlmModelSelector.Invalidate(cache, tenantId);
}
