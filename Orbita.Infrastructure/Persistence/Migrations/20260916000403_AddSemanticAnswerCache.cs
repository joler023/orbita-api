using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticAnswerCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "semantic_cache_threshold",
                table: "ai_agents",
                type: "numeric(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_answer_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_answer_cache", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_answer_cache_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_answer_cache_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_answer_cache_agent_id",
                table: "agent_answer_cache",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_answer_cache_tenant_agent_fingerprint",
                table: "agent_answer_cache",
                columns: new[] { "tenant_id", "agent_id", "fingerprint" });

            // Tenant-scoped like every other table an assistant reads at runtime. Reusing
            // an answer across tenants would be the worst failure this product has, so it
            // is refused by Postgres and not only by the WHERE clause above it.
            migrationBuilder.Sql("""
                ALTER TABLE agent_answer_cache ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON agent_answer_cache
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_answer_cache");

            migrationBuilder.DropColumn(
                name: "semantic_cache_threshold",
                table: "ai_agents");
        }
    }
}
