using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // knowledge_chunks.embedding is a pgvector column. scripts/init-extensions.sql
            // already creates this for the local Docker database and the Testcontainers
            // image ships it, but a fresh environment provisioned another way would not —
            // and a missing extension here fails the migration with a confusing type
            // error rather than an obvious one.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "ai_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    temperature = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    max_tokens = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_agents", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_agents_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    tokens_in = table.Column<int>(type: "integer", nullable: false),
                    tokens_out = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    finish_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_runs_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_runs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_docs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    chunk_count = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_docs", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_docs_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_docs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(768)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_chunks_knowledge_docs_doc_id",
                        column: x => x.doc_id,
                        principalTable: "knowledge_docs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_chunks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agents_tenant_enabled",
                table: "ai_agents",
                columns: new[] { "tenant_id", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_agent_id",
                table: "ai_runs",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_runs_billing",
                table: "ai_runs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_runs_conversation",
                table: "ai_runs",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_doc_id",
                table: "knowledge_chunks",
                column: "doc_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_tenant_doc",
                table: "knowledge_chunks",
                columns: new[] { "tenant_id", "doc_id" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_docs_agent_id",
                table: "knowledge_docs",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_docs_tenant_agent_created",
                table: "knowledge_docs",
                columns: new[] { "tenant_id", "agent_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_docs_tenant_status",
                table: "knowledge_docs",
                columns: new[] { "tenant_id", "status" });

            // Layer 2 of the isolation rule in orbita-schema.dbml (layer 1 is the EF query
            // filter in OrbitaDbContext, layer 3 the tenant_id-first indexes above). Same
            // policy shape as memberships: NULLIF guards the empty string, so a session
            // with no app.tenant_id set matches nothing at all rather than erroring — and
            // "nothing" is the safe answer.
            //
            // This binds `orbita_app`, the role the API actually connects as. Postgres
            // exempts superusers and table owners from RLS unconditionally, so migrations
            // (which run as the owner) are unaffected, and the ALTER DEFAULT PRIVILEGES
            // from ORB-A09's migration already grants orbita_app access to these tables.
            foreach (var table in new[] { "ai_agents", "knowledge_docs", "knowledge_chunks", "ai_runs" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;

                    CREATE POLICY tenant_isolation ON {table}
                        USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }

            // ai_runs is append-only in the same sense as audit_log: a run is a fact about
            // a call that already happened, and ORB-A13's metering and ORB-A12's billing
            // are computed from it. Revoking UPDATE and DELETE from the app's own role
            // enforces that at the database rather than by convention — the same treatment
            // audit_log gets in ORB-A15's migration.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON ai_runs FROM orbita_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "ai_agents", "knowledge_docs", "knowledge_chunks", "ai_runs" })
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.DropTable(
                name: "ai_runs");

            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropTable(
                name: "knowledge_docs");

            migrationBuilder.DropTable(
                name: "ai_agents");
        }
    }
}
