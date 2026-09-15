namespace Orbita.Application.Crm;

public sealed class ContactAlreadyExistsException()
    : Exception("A contact with this phone or Instagram already exists.");
