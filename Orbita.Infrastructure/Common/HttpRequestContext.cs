using Microsoft.AspNetCore.Http;
using Orbita.Application.Common;

namespace Orbita.Infrastructure.Common;

/// <summary>Reads IP/user-agent off the ambient HttpContext, when there is one (ORB-A15).</summary>
public sealed class HttpRequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
