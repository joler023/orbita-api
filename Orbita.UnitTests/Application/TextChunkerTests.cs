using Orbita.Application.Ai;

namespace Orbita.UnitTests.Application;

public sealed class TextChunkerTests
{
    private readonly TextChunker _sut = new();

    [Fact]
    public void Empty_text_produces_no_chunks()
    {
        Assert.Empty(_sut.Split(string.Empty));
        Assert.Empty(_sut.Split("   \n\n  "));
    }

    [Fact]
    public void A_short_document_stays_in_one_chunk()
    {
        var chunks = _sut.Split("Vendemos pan artesanal.\n\nAbrimos de 7am a 7pm.");

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Index);
        Assert.Contains("pan artesanal", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("7am a 7pm", chunk.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunks_are_numbered_in_document_order()
    {
        var chunks = _sut.Split(ParagraphsTotalling(5_000));

        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.Index));
    }

    [Fact]
    public void Consecutive_chunks_overlap_so_an_answer_on_the_boundary_is_still_findable()
    {
        var chunks = _sut.Split(ParagraphsTotalling(6_000));

        Assert.True(chunks.Count >= 2);

        // The tail of each chunk reappears at the head of the next one.
        for (var i = 1; i < chunks.Count; i++)
        {
            var previousTail = chunks[i - 1].Content[^80..];
            var overlapWord = previousTail.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();

            Assert.Contains(overlapWord, chunks[i].Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Paragraphs_are_kept_whole_rather_than_cut_mid_sentence()
    {
        // A chunk cut mid-sentence embeds badly and displays badly.
        var paragraphs = Enumerable.Range(0, 12)
            .Select(i => $"Párrafo número {i} sobre nuestro catálogo de productos horneados diariamente.");
        var chunks = _sut.Split(string.Join("\n\n", paragraphs));

        foreach (var chunk in chunks)
        {
            Assert.EndsWith(".", chunk.Content.TrimEnd(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_paragraph_longer_than_a_whole_chunk_is_split_without_breaking_words()
    {
        // The fallback path: a scanned PDF dumped as one enormous blob has no paragraph
        // breaks to pack on.
        var words = string.Join(' ', Enumerable.Repeat("palabra", 1_000));

        var chunks = _sut.Split(words);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.DoesNotContain("palabr ", chunk.Content, StringComparison.Ordinal));
        Assert.All(chunks, chunk => Assert.True(chunk.Content.Length <= TextChunker.TargetChunkCharacters));
    }

    [Fact]
    public void No_chunk_exceeds_the_target_size()
    {
        var chunks = _sut.Split(ParagraphsTotalling(20_000));

        Assert.All(chunks, chunk => Assert.True(
            chunk.Content.Length <= TextChunker.TargetChunkCharacters + TextChunker.OverlapCharacters,
            $"Chunk {chunk.Index} was {chunk.Content.Length} characters."));
    }

    [Fact]
    public void Every_chunk_reports_a_token_estimate()
    {
        var chunks = _sut.Split(ParagraphsTotalling(3_000));

        Assert.All(chunks, chunk => Assert.True(chunk.EstimatedTokens > 0));
    }

    [Fact]
    public void Windows_line_endings_are_treated_as_paragraph_breaks()
    {
        // A .txt saved on Windows must chunk the same as one saved on Linux.
        var chunks = _sut.Split("Primer párrafo.\r\n\r\nSegundo párrafo.");

        Assert.Single(chunks);
        Assert.DoesNotContain("\r", chunks[0].Content, StringComparison.Ordinal);
    }

    private static string ParagraphsTotalling(int approximateCharacters)
    {
        const string paragraph = "Nuestra panadería prepara pan artesanal todos los días con masa madre "
            + "fermentada durante veinticuatro horas, y también atendemos pedidos especiales.";

        var count = Math.Max(2, approximateCharacters / paragraph.Length);

        return string.Join("\n\n", Enumerable.Range(0, count).Select(i => $"{paragraph} Detalle {i}."));
    }
}
