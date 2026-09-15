namespace Orbita.Application.Channels;

public sealed class TemplateAlreadyExistsException() : Exception("A template with that name and language is already registered for this channel account.");
