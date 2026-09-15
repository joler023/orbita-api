namespace Orbita.Application.Crm;

public sealed class InvalidStageRelocateException()
    : Exception("Opportunities must move to a different stage in the same pipeline.");
