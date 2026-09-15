namespace Orbita.Application.Ai;

/// <summary>
/// Splits a document's text into the pieces that get embedded and searched (ORB-C02:
/// "troceo con solapamiento").
/// </summary>
public interface ITextChunker
{
    /// <returns>Chunks in document order. Empty when the text has no usable content.</returns>
    IReadOnlyList<TextChunk> Split(string text);
}

/// <param name="Index">Position in the document, preserved so a search hit can cite where it came from.</param>
/// <param name="Content">The text to embed.</param>
/// <param name="EstimatedTokens">
/// Rough token count, used for reporting only. Real tokenization is provider-specific,
/// so this is deliberately an estimate rather than a false precision.
/// </param>
public sealed record TextChunk(int Index, string Content, int EstimatedTokens);
