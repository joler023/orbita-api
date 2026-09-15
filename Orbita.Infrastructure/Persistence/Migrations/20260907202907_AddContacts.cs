using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "contact_id",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contact_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    field_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_field_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_contact_field_definitions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    instagram_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    custom_fields = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_contacts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_contact_id",
                table: "opportunities",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_tenant_contact",
                table: "opportunities",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_field_definitions_tenant_key",
                table: "contact_field_definitions",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_instagram",
                table: "contacts",
                columns: new[] { "tenant_id", "instagram_username" },
                unique: true,
                filter: "instagram_username IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_name",
                table: "contacts",
                columns: new[] { "tenant_id", "display_name" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_phone",
                table: "contacts",
                columns: new[] { "tenant_id", "phone" },
                unique: true,
                filter: "phone IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_contacts_contact_id",
                table: "opportunities",
                column: "contact_id",
                principalTable: "contacts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(
                """
                ALTER TABLE contacts ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON contacts
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE contact_field_definitions ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON contact_field_definitions
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON contact_field_definitions;
                ALTER TABLE contact_field_definitions DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON contacts;
                ALTER TABLE contacts DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_contacts_contact_id",
                table: "opportunities");

            migrationBuilder.DropTable(
                name: "contact_field_definitions");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_contact_id",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "ix_opportunities_tenant_contact",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "contact_id",
                table: "opportunities");
        }
    }
}
