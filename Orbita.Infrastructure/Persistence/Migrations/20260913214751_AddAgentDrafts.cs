using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ORB-C10. Unpublished edits to an assistant's configuration, so that saving the
    /// configuration screen never changes what live customers are being told — "nadie
    /// debería editar en caliente un agente que está atendiendo clientes".
    /// </summary>
    public partial class AddAgentDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_agent_drafts",
                columns: table => new
                {
                    // The agent's own id, not an id of its own: an assistant has at most
                    // one pending draft, so saving twice replaces rather than accumulates.
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    personality = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tools = table.Column<string>(type: "jsonb", nullable: false),
                    formality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verbosity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    energy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_agent_drafts", x => x.agent_id);
                    table.ForeignKey(
                        name: "FK_ai_agent_drafts_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_agent_drafts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Layer 3 of the isolation rule, tenant_id first: the list screen reads every
            // draft a tenant has in one query, to badge its rows without an N+1.
            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_drafts_tenant",
                table: "ai_agent_drafts",
                column: "tenant_id");

            // Layer 2 — same policy shape as every other tenant-scoped table. A draft is
            // the most private thing in this feature: it is what an owner is still
            // deciding, and an unscoped session must see nothing rather than everything.
            migrationBuilder.Sql("""
                ALTER TABLE ai_agent_drafts ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON ai_agent_drafts
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON ai_agent_drafts;
                ALTER TABLE ai_agent_drafts DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "ai_agent_drafts");
        }
    }
}
