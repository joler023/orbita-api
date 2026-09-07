namespace Orbita.Application.Identity;

/// <summary>A tenant must always keep at least one active Owner (ORB-A08).</summary>
public sealed class CannotRemoveLastOwnerException() : Exception("A tenant must always have at least one owner.");
