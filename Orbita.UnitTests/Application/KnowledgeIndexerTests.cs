using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

/// <summary>
/// The indexer's whole job beyond looping is deciding what a failure means, so that is
/// what these pin down.
/// </summary>
public sealed class KnowledgeIndexerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly Mock<IKnowledgeIndexingQueue> _queue = new();
    private readonly Mock<IKnowledgeDocumentRepository> _documents = new();
    private readonly Mock<IDocumentChunkBuilder> _chunkBuilder = new();
    private readonly Mock<ITenantContextSetter> _tenantContextSetter = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly KnowledgeDocument _document = KnowledgeDocument.Create(
        TenantId, Guid.NewGuid(), "Catálogo", KnowledgeDocSourceType.Upload, "key.pdf", Now);

    private readonly KnowledgeIndexer _sut;

    public KnowledgeIndexerTests()
    {
        _queue.Setup(q => q.PeekAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([KnowledgeIndexingQueueEntry.For(_document, Now)]);

        _documents.Setup(d => d.GetByIdAsync(TenantId, _document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_document);

        // QueryInTenantScopeAsync just runs the query in these tests; the real transaction
        // behaviour is covered by the integration suite.
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(
                It.IsAny<Func<CancellationToken, Task<KnowledgeDocument?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<KnowledgeDocument?>> query, CancellationToken ct) => query(ct));

        _sut = new KnowledgeIndexer(
            _queue.Object,
            _documents.Object,
            _chunkBuilder.Object,
            _tenantContextSetter.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now),
            NullLogger<KnowledgeIndexer>.Instance);
    }

    [Fact]
    public async Task A_document_that_indexes_cleanly_ends_up_indexed_with_its_chunk_count()
    {
        _chunkBuilder.Setup(b => b.BuildAsync(_document, It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var indexed = await _sut.IndexPendingAsync(CancellationToken.None);

        Assert.Equal(1, indexed);
        Assert.Equal(KnowledgeDocStatus.Indexed, _document.Status);
        Assert.Equal(7, _document.ChunkCount);
        Assert.Equal(Now, _document.IndexedAt);
        Assert.Null(_document.FailureReason);
    }

    [Fact]
    public async Task The_workers_tenant_scope_is_set_before_anything_tenant_scoped_is_touched()
    {
        // Without this the RLS policy returns zero rows and the pass silently does nothing.
        _chunkBuilder.Setup(b => b.BuildAsync(_document, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _sut.IndexPendingAsync(CancellationToken.None);

        _tenantContextSetter.Verify(s => s.SetTenant(TenantId), Times.Once);
    }

    [Fact]
    public async Task An_unreadable_document_fails_with_a_reason_the_user_can_act_on()
    {
        _chunkBuilder
            .Setup(b => b.BuildAsync(_document, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("No pudimos leer el PDF. Puede estar dañado."));

        var indexed = await _sut.IndexPendingAsync(CancellationToken.None);

        Assert.Equal(0, indexed);
        Assert.Equal(KnowledgeDocStatus.Failed, _document.Status);
        Assert.Equal("No pudimos leer el PDF. Puede estar dañado.", _document.FailureReason);
    }

    [Fact]
    public async Task A_provider_outage_leaves_the_document_pending_rather_than_blaming_it()
    {
        // Telling someone their catalogue is corrupt because the embedding provider was
        // restarting would send them off to re-upload a perfectly good file.
        _chunkBuilder
            .Setup(b => b.BuildAsync(_document, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LlmProviderException("ollama", "connection refused", isTransient: true));

        var indexed = await _sut.IndexPendingAsync(CancellationToken.None);

        Assert.Equal(0, indexed);
        Assert.Equal(KnowledgeDocStatus.Pending, _document.Status);
        Assert.Null(_document.FailureReason);
        // Still queued: the next pass retries it.
        _queue.Verify(q => q.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unexpected_failure_still_yields_a_readable_reason_not_a_type_name()
    {
        _chunkBuilder
            .Setup(b => b.BuildAsync(_document, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("embedding has 1536 dimensions"));

        await _sut.IndexPendingAsync(CancellationToken.None);

        Assert.Equal(KnowledgeDocStatus.Failed, _document.Status);
        Assert.DoesNotContain("Exception", _document.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("documento", _document.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_queue_costs_one_query_and_nothing_else()
    {
        _queue.Setup(q => q.PeekAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        Assert.Equal(0, await _sut.IndexPendingAsync(CancellationToken.None));
        _chunkBuilder.Verify(b => b.BuildAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        _tenantContextSetter.Verify(s => s.SetTenant(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task A_finished_document_is_dropped_from_the_queue_without_being_reindexed()
    {
        // What a crash between "indexed" and "dequeued" leaves behind. Re-embedding it
        // would cost real money for no reason.
        _document.MarkIndexed(3, Now);

        var indexed = await _sut.IndexPendingAsync(CancellationToken.None);

        Assert.Equal(0, indexed);
        _chunkBuilder.Verify(b => b.BuildAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        _queue.Verify(q => q.RemoveAsync(_document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_deleted_document_is_dropped_from_the_queue()
    {
        _documents.Setup(d => d.GetByIdAsync(TenantId, _document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeDocument?)null);

        Assert.Equal(0, await _sut.IndexPendingAsync(CancellationToken.None));
        _queue.Verify(q => q.RemoveAsync(_document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Documents_from_different_tenants_each_get_their_own_scope()
    {
        var otherTenantId = Guid.NewGuid();
        var otherDocument = KnowledgeDocument.Create(
            otherTenantId, Guid.NewGuid(), "Otro", KnowledgeDocSourceType.Manual, "otro.txt", Now);

        _queue.Setup(q => q.PeekAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                KnowledgeIndexingQueueEntry.For(_document, Now),
                KnowledgeIndexingQueueEntry.For(otherDocument, Now),
            ]);
        _documents.Setup(d => d.GetByIdAsync(otherTenantId, otherDocument.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherDocument);
        _chunkBuilder.Setup(b => b.BuildAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Assert.Equal(2, await _sut.IndexPendingAsync(CancellationToken.None));

        _tenantContextSetter.Verify(s => s.SetTenant(TenantId), Times.Once);
        _tenantContextSetter.Verify(s => s.SetTenant(otherTenantId), Times.Once);
    }
}
