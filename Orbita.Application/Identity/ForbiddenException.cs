namespace Orbita.Application.Identity;

/// <summary>The caller is authenticated but not allowed to perform this action.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
