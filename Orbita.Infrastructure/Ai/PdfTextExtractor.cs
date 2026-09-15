using System.Text;
using Orbita.Application.Ai;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Reads text out of a PDF with PdfPig (Apache-2.0, no native dependencies — it runs the
/// same on a developer's Windows laptop and in a Linux container).
///
/// Pages are separated by a blank line so <c>TextChunker</c>, which packs whole
/// paragraphs, treats a page break as a boundary rather than gluing the last line of one
/// page to the first line of the next.
///
/// A scanned PDF — pixels, no text layer — extracts to nothing. That is reported as a
/// readable failure rather than an empty index, because "indexed, 0 chunks" would look
/// like success to the person who uploaded it.
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public async Task<string> ExtractAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // PdfPig needs random access; an upload stream is not necessarily seekable.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        try
        {
            using var document = PdfDocument.Open(buffer);
            var text = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ContentOrderTextExtractor follows the PDF's own content stream order,
                // which keeps multi-column layouts (price lists, catalogues) readable
                // instead of interleaving the columns line by line.
                var pageText = ContentOrderTextExtractor.GetText(page);

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    text.Append(pageText.Trim()).Append("\n\n");
                }
            }

            return text.ToString();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException(
                "No pudimos leer el PDF. Puede estar dañado o protegido con contraseña.", exception);
        }
    }
}
