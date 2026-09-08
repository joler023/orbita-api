using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    waba_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone_e164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    credentials_ref = table.Column<string>(type: "text", nullable: false),
                    webhook_secret = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_channel_accounts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channel_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ciphertext = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_credentials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_accounts_tenant_kind",
                table: "channel_accounts",
                columns: new[] { "tenant_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ux_channel_external",
                table: "channel_accounts",
                columns: new[] { "kind", "external_id" },
                unique: true);

            // No ROW LEVEL SECURITY on either table, deliberately — the same exception
            // already made for subscriptions and the Identity tokens (ORB-B01):
            //   - channel_accounts is looked up by (kind, external_id) from an inbound
            //     Meta webhook before any tenant is known, and RLS returns zero rows
            //     when no app.tenant_id is set in the session. Application code checks
            //     TenantId explicitly instead (see ChannelAccount / WhatsAppChannelService).
            //   - channel_credentials is the local stand-in for AWS Secrets Manager and
            //     is addressed by opaque reference only; it has no tenant_id at all.
            // orbita_app still gets SELECT/INSERT/UPDATE/DELETE on both through the
            // ALTER DEFAULT PRIVILEGES set up in AddUsersAndMemberships.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_accounts");

            migrationBuilder.DropTable(
                name: "channel_credentials");
        }
    }
}
