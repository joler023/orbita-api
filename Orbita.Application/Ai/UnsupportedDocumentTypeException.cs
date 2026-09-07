namespace Orbita.Application.Ai;

/// <summary>
/// The uploaded file is not one of the formats ORB-C02 accepts (PDF, DOCX, TXT, MD).
/// The message names the extension so the API can hand the user something actionable
/// rather than a generic 400.
/// </summary>
public sealed class UnsupportedDocumentTypeException(string extension)
    : Exception($"Documents of type '{extension}' are not supported. Upload a PDF, DOCX, TXT or Markdown file.")
{
    public string Extension { get; } = extension;
}
