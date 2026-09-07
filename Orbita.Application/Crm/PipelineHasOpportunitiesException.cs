namespace Orbita.Application.Crm;

public sealed class PipelineHasOpportunitiesException()
    : Exception("A pipeline that still has opportunities cannot be deleted.");
