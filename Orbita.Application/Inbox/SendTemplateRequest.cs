namespace Orbita.Application.Inbox;

public sealed record SendTemplateRequest(Guid TemplateId, IReadOnlyList<string> Variables);
