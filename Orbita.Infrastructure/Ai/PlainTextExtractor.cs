using System.Text;
using Orbita.Application.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// Reads <c>.txt</c> and <c>.md</c>, which need no parsing — Markdown is fed to the
/// embedding model with its syntax intact, because headings and list markers are real
/// signal about how the document is organized.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".txt", ".md"];

    public async Task<string> ExtractAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // detectEncodingFromByteOrderMarks: a file saved from Windows Notepad as UTF-16
        // would otherwise come back as mojibake, and the user would see a document that
        // "indexed fine" but matches nothing.
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        return await reader.ReadToEndAsync(cancellationToken);
    }
}
