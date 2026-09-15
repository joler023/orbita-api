namespace Orbita.Application.Crm;

public sealed class CannotDeleteLastPipelineException()
    : Exception("A tenant must keep at least one pipeline.");
