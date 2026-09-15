namespace Orbita.Application.Ai;

/// <summary>
/// Publish was called with no pending draft. A conflict rather than a silent success: the
/// screen's Publish button should be disabled when there is nothing to publish, so reaching
/// here means the UI and the server disagree about what is pending — worth surfacing.
/// </summary>
public sealed class NothingToPublishException()
    : Exception("This assistant has no unpublished changes.");
