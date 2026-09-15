using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantModelPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_model_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    task = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_model_preferences", x => x.id);
                    table.ForeignKey(
                        name: "FK_tenant_model_preferences_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Layer 2 of the isolation rule — same policy shape as every other
            // tenant-scoped table. A tenant's model choice is not secret, but it is theirs,
            // and an unscoped session must see nothing rather than everything.
            migrationBuilder.Sql("""
                ALTER TABLE tenant_model_preferences ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON tenant_model_preferences
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);

            migrationBuilder.CreateIndex(
                name: "ux_tenant_model_preferences_tenant_provider_task",
                table: "tenant_model_preferences",
                columns: new[] { "tenant_id", "provider_name", "task" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON tenant_model_preferences;
                ALTER TABLE tenant_model_preferences DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "tenant_model_preferences");
        }
    }
}
