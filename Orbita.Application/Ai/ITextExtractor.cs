namespace Orbita.Application.Ai;

/// <summary>
/// Pulls plain text out of one document format (ORB-C02 accepts PDF, DOCX, TXT and
/// Markdown). One implementation per format, resolved by extension — adding a format is
/// a new class plus a DI registration, never an edit to a switch statement someone has
/// to remember to update.
/// </summary>
public interface ITextExtractor
{
    /// <summary>Lower-case extensions this extractor handles, including the dot (e.g. <c>.pdf</c>).</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <exception cref="InvalidDataException">The file is corrupt or not really this format.</exception>
    Task<string> ExtractAsync(Stream content, CancellationToken cancellationToken);
}
