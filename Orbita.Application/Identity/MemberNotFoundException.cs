namespace Orbita.Application.Identity;

/// <summary>No active membership with that id exists in the given tenant (ORB-A08).</summary>
public sealed class MemberNotFoundException() : Exception("Member not found.");
