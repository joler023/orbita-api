using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Orbita.Application.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Reads text out of a <c>.docx</c> with the Open XML SDK (MIT).
///
/// Only paragraphs are pulled, one per line, which is what
/// <c>TextChunker</c> splits on. Tables are flattened into their cell text — a price
/// list in a table is exactly the kind of thing a customer asks about, so dropping it
/// would quietly lose the most useful part of many documents.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".docx"];

    public async Task<string> ExtractAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        try
        {
            using var document = WordprocessingDocument.Open(buffer, isEditable: false);

            var body = document.MainDocumentPart?.Document.Body;

            if (body is null)
            {
                return string.Empty;
            }

            var text = new StringBuilder();

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = paragraph.InnerText;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    text.Append(line.Trim()).Append("\n\n");
                }
            }

            return text.ToString();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException(
                "No pudimos leer el documento de Word. Puede estar dañado. Prueba a guardarlo de nuevo como .docx.",
                exception);
        }
    }
}
