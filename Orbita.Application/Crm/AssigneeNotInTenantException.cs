namespace Orbita.Application.Crm;

public sealed class AssigneeNotInTenantException()
    : Exception("The assigned person is not an active member of this organization.");
