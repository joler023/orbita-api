using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "handoff_reason",
                table: "conversations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "handoff_requested_at",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handoff_summary",
                table: "conversations",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "was_handoff",
                table: "ai_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "handoff_reply",
                table: "ai_agents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "Listo: dejo de responderte yo y la conversación queda para alguien del equipo.");

            migrationBuilder.CreateIndex(
                name: "ix_conv_handoff_queue",
                table: "conversations",
                columns: new[] { "tenant_id", "handoff_requested_at" },
                filter: "handoff_requested_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conv_handoff_queue",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "handoff_reason",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "handoff_requested_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "handoff_summary",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "was_handoff",
                table: "ai_runs");

            migrationBuilder.DropColumn(
                name: "handoff_reply",
                table: "ai_agents");
        }
    }
}
