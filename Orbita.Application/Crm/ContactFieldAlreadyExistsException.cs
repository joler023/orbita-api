namespace Orbita.Application.Crm;

public sealed class ContactFieldAlreadyExistsException()
    : Exception("A custom field with this key already exists.");
