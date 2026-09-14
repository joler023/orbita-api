using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ORB-C03's HNSW index over <c>knowledge_chunks.embedding</c>.
    ///
    /// Hand-written rather than generated: an index with an access method and an operator
    /// class is not expressible through EF Core's model, which is exactly what
    /// orbita-schema.dbml's note on <c>knowledge_chunks</c> anticipates.
    ///
    /// <para><b>Known limit — read before chasing a slow search.</b> Every search filters
    /// by <c>tenant_id</c>, and with that predicate present Postgres does not use this
    /// index: it narrows by tenant through <c>ix_knowledge_chunks_tenant_doc</c> and sorts
    /// the survivors by distance. An HNSW index cannot combine with an unrelated equality
    /// filter, so the planner is making the right call — sorting one tenant's chunks beats
    /// walking a graph built over every tenant's.</para>
    ///
    /// <para>That is fine while a single tenant's corpus is small: sorting a few thousand
    /// 1536-component vectors is milliseconds. It stops being fine somewhere in the tens
    /// of thousands <em>per tenant</em>, which is where ORB-C03's "menos de 200 ms con
    /// 100.000 fragmentos" would start to bite. The options at that point, cheapest
    /// first: pgvector's iterative index scans (<c>hnsw.iterative_scan</c>), a partial
    /// HNSW index per large tenant, or partitioning <c>knowledge_chunks</c> by tenant.
    /// None is worth doing before there is a tenant big enough to need it.</para>
    ///
    /// The index is created anyway because it costs little, and because the day the
    /// search stops carrying a tenant predicate — a cross-tenant admin tool, an offline
    /// evaluation job — it is what keeps that query from scanning the whole table.
    /// </summary>
    public partial class AddKnowledgeChunkHnswIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // vector_cosine_ops has to match the operator the query uses (<=>). An index
            // built for a different distance is simply never chosen, and the search
            // silently degrades to a sequential scan over every chunk in the database —
            // no error, just a query that gets slower as the product succeeds. That is
            // the failure ORB-C03's "menos de 200 ms con 100.000 fragmentos" exists to
            // catch.
            //
            // HNSW rather than IVFFlat because it needs no training pass over an existing
            // corpus and stays accurate as rows arrive one document at a time, which is
            // how knowledge actually gets added here.
            migrationBuilder.Sql("""
                CREATE INDEX ix_knowledge_chunks_embedding_hnsw
                    ON knowledge_chunks
                    USING hnsw (embedding vector_cosine_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql("DROP INDEX IF EXISTS ix_knowledge_chunks_embedding_hnsw;");
    }
}
