using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "template_variables",
                table: "messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meta_template_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rejected_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_templates", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_templates_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_channel_account_id_meta_template_name_lan~",
                table: "message_templates",
                columns: new[] { "channel_account_id", "meta_template_name", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_tenant_id_status",
                table: "message_templates",
                columns: new[] { "tenant_id", "status" });

            // Standard tenant isolation — message_templates is always read/written with
            // the tenant already known (an Owner/Admin managing their own templates),
            // unlike channel_accounts/inbound_webhook_events/outbox_events.
            migrationBuilder.Sql(
                """
                ALTER TABLE message_templates ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON message_templates
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropColumn(
                name: "template_variables",
                table: "messages");
        }
    }
}
