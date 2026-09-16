using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingRulesAndBusinessHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "business_hours",
                table: "ai_agents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "routing_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    keyword = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_routing_rules_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_routing_rules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_routing_rules_agent_id",
                table: "routing_rules",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_routing_rules_tenant_position",
                table: "routing_rules",
                columns: new[] { "tenant_id", "position" });

            // Layer 2 of the isolation rule, same policy as memberships. orbita_app gets
            // read/write through the default privileges set up in AddUsersAndMemberships.
            migrationBuilder.Sql("""
                ALTER TABLE routing_rules ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON routing_rules
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routing_rules");

            migrationBuilder.DropColumn(
                name: "business_hours",
                table: "ai_agents");
        }
    }
}
