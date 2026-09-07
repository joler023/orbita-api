namespace Orbita.Application.Common;

/// <summary>
/// The ambient HTTP request an application service is running inside of, if any — used
/// by <see cref="Audit.IAuditLogger"/> to capture where an action came from (ORB-A15's
/// audit_log.ip/user_agent). Both are null outside a real HTTP request (a background
/// job, a test that doesn't go through the pipeline), which the schema already allows.
/// </summary>
public interface IRequestContext
{
    string? IpAddress { get; }

    string? UserAgent { get; }
}
