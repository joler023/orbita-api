using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Domain;

public sealed class KnowledgeChunkTests
{
    private static float[] ValidEmbedding() => new float[KnowledgeChunk.EmbeddingDimensions];

    [Fact]
    public void A_chunk_keeps_its_position_so_a_search_hit_can_cite_it()
    {
        var chunk = KnowledgeChunk.Create(Guid.NewGuid(), Guid.NewGuid(), 3, "pan artesanal", 4, ValidEmbedding());

        Assert.Equal(3, chunk.ChunkIndex);
        Assert.Equal("pan artesanal", chunk.Content);
        Assert.Equal(4, chunk.TokenCount);
        Assert.Equal(KnowledgeChunk.EmbeddingDimensions, chunk.Embedding.Length);
    }

    [Fact]
    public void An_embedding_of_the_wrong_size_is_rejected_with_an_explanation()
    {
        // This is the guard that catches "somebody changed the embedding model" before it
        // becomes a Postgres type error nobody can read.
        var wrongSize = new float[1536];

        var failure = Assert.Throws<ArgumentException>(
            () => KnowledgeChunk.Create(Guid.NewGuid(), Guid.NewGuid(), 0, "hola", 1, wrongSize));

        Assert.Contains("1536", failure.Message, StringComparison.Ordinal);
        Assert.Contains(KnowledgeChunk.EmbeddingDimensions.ToString(), failure.Message, StringComparison.Ordinal);
        Assert.Contains("reindex", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_chunk_needs_content()
        => Assert.Throws<ArgumentException>(
            () => KnowledgeChunk.Create(Guid.NewGuid(), Guid.NewGuid(), 0, "   ", null, ValidEmbedding()));

    [Fact]
    public void A_negative_position_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => KnowledgeChunk.Create(Guid.NewGuid(), Guid.NewGuid(), -1, "hola", null, ValidEmbedding()));

    [Fact]
    public void The_embedding_is_copied_so_the_caller_cannot_mutate_it_afterwards()
    {
        var source = ValidEmbedding();
        var chunk = KnowledgeChunk.Create(Guid.NewGuid(), Guid.NewGuid(), 0, "hola", null, source);

        source[0] = 99f;

        Assert.Equal(0f, chunk.Embedding[0]);
    }
}
