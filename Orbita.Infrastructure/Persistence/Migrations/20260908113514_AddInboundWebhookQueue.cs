using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundWebhookQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_webhook_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_webhook_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbound_webhook_events_payload_hash",
                table: "inbound_webhook_events",
                column: "payload_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_webhook_events_status_id",
                table: "inbound_webhook_events",
                columns: new[] { "status", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_inbound_webhook_events_tenant_id_status_received_at",
                table: "inbound_webhook_events",
                columns: new[] { "tenant_id", "status", "received_at" });

            // No ROW LEVEL SECURITY, deliberately — same exception as channel_accounts
            // (ORB-B01): ingestion resolves the owning tenant from the webhook itself,
            // there is no ambient app.tenant_id in the session at insert time, and RLS
            // would return zero rows / block the insert if it were enabled. orbita_app
            // still gets SELECT/INSERT/UPDATE/DELETE through the ALTER DEFAULT
            // PRIVILEGES set up in AddUsersAndMemberships.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_webhook_events");
        }
    }
}
