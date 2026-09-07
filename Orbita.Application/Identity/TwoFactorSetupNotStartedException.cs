namespace Orbita.Application.Identity;

/// <summary>Confirming setup requires having called BeginSetupAsync first (ORB-A11).</summary>
public sealed class TwoFactorSetupNotStartedException() : Exception("Two-factor setup has not been started for this user.");
