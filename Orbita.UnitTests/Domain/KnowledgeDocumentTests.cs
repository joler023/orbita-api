using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Domain;

public sealed class KnowledgeDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static KnowledgeDocument NewDocument()
        => KnowledgeDocument.Create(Guid.NewGuid(), Guid.NewGuid(), " Catálogo 2026 ", KnowledgeDocSourceType.Upload, "key.pdf", Now);

    [Fact]
    public void A_new_document_starts_pending_with_nothing_indexed()
    {
        var document = NewDocument();

        Assert.Equal(KnowledgeDocStatus.Pending, document.Status);
        Assert.Equal(0, document.ChunkCount);
        Assert.Null(document.IndexedAt);
        Assert.Null(document.FailureReason);
        Assert.Equal("Catálogo 2026", document.Title);
    }

    [Fact]
    public void A_document_needs_a_title()
        => Assert.Throws<ArgumentException>(() =>
            KnowledgeDocument.Create(Guid.NewGuid(), Guid.NewGuid(), "   ", KnowledgeDocSourceType.Manual, null, Now));

    [Fact]
    public void Retrying_a_failed_document_clears_the_previous_reason()
    {
        // Otherwise the UI would keep showing yesterday's error next to a green status.
        var document = NewDocument();
        document.MarkFailed("El PDF está dañado.");

        document.MarkProcessing();

        Assert.Equal(KnowledgeDocStatus.Processing, document.Status);
        Assert.Null(document.FailureReason);
    }

    [Fact]
    public void Indexing_records_the_chunk_count_and_when_it_finished()
    {
        var document = NewDocument();
        document.MarkProcessing();

        document.MarkIndexed(42, Now);

        Assert.Equal(KnowledgeDocStatus.Indexed, document.Status);
        Assert.Equal(42, document.ChunkCount);
        Assert.Equal(Now, document.IndexedAt);
    }

    [Fact]
    public void Failing_resets_the_chunk_count_so_the_ui_never_shows_stale_progress()
    {
        var document = NewDocument();
        document.MarkIndexed(42, Now);

        document.MarkFailed("No se pudo extraer texto.");

        Assert.Equal(KnowledgeDocStatus.Failed, document.Status);
        Assert.Equal(0, document.ChunkCount);
        Assert.Null(document.IndexedAt);
        Assert.Equal("No se pudo extraer texto.", document.FailureReason);
    }

    [Fact]
    public void A_failure_must_carry_a_reason()
        => Assert.Throws<ArgumentException>(() => NewDocument().MarkFailed("  "));

    [Fact]
    public void Reindexing_puts_the_document_back_in_the_queue_and_forgets_the_old_result()
    {
        var document = NewDocument();
        document.MarkIndexed(42, Now);

        document.MarkForReindex();

        Assert.Equal(KnowledgeDocStatus.Pending, document.Status);
        Assert.Equal(0, document.ChunkCount);
        Assert.Null(document.IndexedAt);
    }
}
