namespace Orbita.Application.Inbox;

/// <summary>The message isn't Failed, or its error isn't one MetaErrorCatalog considers transient (ORB-B08).</summary>
public sealed class MessageNotRetryableException() : Exception("This message cannot be retried.");
