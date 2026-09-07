using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    diff = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_log_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_audit_log_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_actor_id",
                table: "audit_log",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_tenant_id_created_at",
                table: "audit_log",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_tenant_id_entity_type_entity_id",
                table: "audit_log",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.Sql(
                """
                ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON audit_log
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                -- orbita-schema.dbml: "Append-only: sin UPDATE ni DELETE" — orbita_app
                -- was already granted UPDATE/DELETE on every table via the blanket
                -- ALTER DEFAULT PRIVILEGES in the AddUsersAndMemberships migration;
                -- this narrows it back off for this one table specifically, enforcing
                -- the invariant at the database level instead of only by convention.
                REVOKE UPDATE, DELETE ON audit_log FROM orbita_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");
        }
    }
}
