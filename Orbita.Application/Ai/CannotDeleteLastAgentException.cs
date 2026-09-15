namespace Orbita.Application.Ai;

/// <summary>
/// A tenant always keeps at least one assistant, the same way ORB-A08 guarantees at least
/// one Owner. Screen 2.5 has no "create your first assistant" empty state by design — one
/// is seeded at registration — so deleting the last one would strand the owner on a screen
/// that cannot get them back.
/// </summary>
public sealed class CannotDeleteLastAgentException()
    : Exception("A tenant must keep at least one AI assistant.");
