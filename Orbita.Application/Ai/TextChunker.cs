using System.Text;

namespace Orbita.Application.Ai;

/// <summary>
/// Splits text on paragraph boundaries, packing whole paragraphs into chunks of roughly
/// <see cref="TargetChunkCharacters"/> and repeating the tail of each chunk at the head
/// of the next.
///
/// Why paragraphs and not a fixed character count: a chunk cut mid-sentence embeds badly.
/// The vector ends up describing half an idea, and the search result shown to the user
/// starts mid-word. Packing whole paragraphs keeps each chunk semantically whole, and the
/// hard split is only a fallback for text that has no paragraph breaks at all (a scanned
/// PDF dumped as one blob, say).
///
/// Why the overlap: an answer that straddles a boundary would otherwise be findable from
/// neither side. Repeating the tail costs a little storage and buys recall — the standard
/// trade for retrieval-augmented generation.
/// </summary>
public sealed class TextChunker : ITextChunker
{
    /// <summary>
    /// About 250 words. Small enough that a hit is precise and cheap to embed, large
    /// enough to carry a whole idea.
    /// </summary>
    public const int TargetChunkCharacters = 1_200;

    /// <summary>Roughly one paragraph of context repeated from the previous chunk.</summary>
    public const int OverlapCharacters = 200;

    /// <summary>
    /// Rough characters-per-token for Spanish and English. Only used for the reported
    /// estimate, never for a limit that would break if it were wrong.
    /// </summary>
    private const int CharactersPerToken = 4;

    public IReadOnlyList<TextChunk> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var paragraphs = SplitIntoParagraphs(text);
        var chunks = new List<TextChunk>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            // A single paragraph longer than a whole chunk cannot be packed; flush what
            // we have and cut it by length instead.
            if (paragraph.Length > TargetChunkCharacters)
            {
                Flush(chunks, current);

                foreach (var piece in SplitLongParagraph(paragraph))
                {
                    chunks.Add(BuildChunk(chunks, piece));
                }

                continue;
            }

            if (current.Length > 0 && current.Length + paragraph.Length + 2 > TargetChunkCharacters)
            {
                Flush(chunks, current);
                current.Append(TailOf(chunks[^1].Content));
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
        }

        Flush(chunks, current);

        return chunks;
    }

    private static void Flush(List<TextChunk> chunks, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        var content = current.ToString().Trim();
        current.Clear();

        if (content.Length > 0)
        {
            chunks.Add(BuildChunk(chunks, content));
        }
    }

    private static TextChunk BuildChunk(List<TextChunk> chunks, string content)
        => new(chunks.Count, content, Math.Max(1, content.Length / CharactersPerToken));

    /// <summary>The trailing slice repeated at the start of the next chunk, cut back to a word boundary.</summary>
    private static string TailOf(string content)
    {
        if (content.Length <= OverlapCharacters)
        {
            return content + "\n\n";
        }

        var tail = content[^OverlapCharacters..];
        var firstSpace = tail.IndexOf(' ', StringComparison.Ordinal);

        return (firstSpace >= 0 ? tail[(firstSpace + 1)..] : tail) + "\n\n";
    }

    private static IEnumerable<string> SplitIntoParagraphs(string text)
        => text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Fallback for text with no paragraph structure: cut by length, backing up to the
    /// last space so words stay intact, and carry the same overlap.
    /// </summary>
    private static IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        var start = 0;

        while (start < paragraph.Length)
        {
            var length = Math.Min(TargetChunkCharacters, paragraph.Length - start);
            var slice = paragraph.Substring(start, length);

            if (start + length < paragraph.Length)
            {
                var lastSpace = slice.LastIndexOf(' ');

                if (lastSpace > TargetChunkCharacters / 2)
                {
                    slice = slice[..lastSpace];
                    length = lastSpace;
                }
            }

            yield return slice.Trim();

            var advance = Math.Max(1, length - OverlapCharacters);
            start += advance;
        }
    }
}
