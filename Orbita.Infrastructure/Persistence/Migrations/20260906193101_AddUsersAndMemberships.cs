using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_user_id",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_memberships_tenant_user",
                table: "memberships",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            // Layer 2 of the isolation rule in orbita-schema.dbml. `current_setting`
            // is called with missing_ok = true so a session that never called
            // set_config('app.tenant_id', ...) resolves to NULL instead of erroring;
            // NULLIF folds the empty string UnitOfWork uses for "no ambient tenant"
            // into NULL too, for the same reason. Either way, tenant_id = NULL is
            // never true, so a session with no tenant default-denies every row
            // instead of raising a cast error. Every write and (eventually) every
            // tenant-scoped read must set that session value first; see
            // Orbita.Infrastructure.Persistence.UnitOfWork.
            migrationBuilder.Sql(
                """
                ALTER TABLE memberships ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON memberships
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);

            // Postgres never applies RLS to the table owner or to a superuser
            // (FORCE ROW LEVEL SECURITY only closes the owner half of that gap, and
            // does nothing for superusers) — so the RLS policy above is decorative
            // unless the application actually connects as some other, ordinary role.
            // `orbita_app` is that role for local dev and for the test suite: it owns
            // nothing and has no elevated attributes, so RLS binds to it for real.
            // Migrations keep running as the superuser/table owner (`orbita` locally),
            // which is the normal shape for DDL versus runtime credentials. In a real
            // environment this role and its grants are provisioned by Terraform
            // (ORB-A02), not by an application migration — this bootstraps the same
            // shape locally so ORB-A09 can be verified end to end without it.
            //
            // Roles live at the cluster level, not the database level, so a role
            // created while migrating one database is still there if another database
            // in the same cluster (or the same database, recreated) is migrated from
            // scratch later — guard the CREATE with an existence check rather than
            // assuming a pristine cluster.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'orbita_app') THEN
                        CREATE ROLE orbita_app WITH LOGIN PASSWORD 'orbita_app_dev_only' NOSUPERUSER NOCREATEDB NOCREATEROLE;
                    END IF;
                END
                $$;

                GRANT USAGE ON SCHEMA public TO orbita_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO orbita_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE orbita IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO orbita_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER DEFAULT PRIVILEGES FOR ROLE orbita IN SCHEMA public
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM orbita_app;
                DROP OWNED BY orbita_app;
                DROP ROLE IF EXISTS orbita_app;

                DROP POLICY IF EXISTS tenant_isolation ON memberships;
                ALTER TABLE memberships DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");
        }
    }
}
