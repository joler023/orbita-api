namespace Orbita.Application.Crm;

public sealed class CannotDeleteLastStageException()
    : Exception("A pipeline must keep at least one stage.");
