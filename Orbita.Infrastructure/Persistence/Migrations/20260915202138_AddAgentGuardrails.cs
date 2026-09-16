using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentGuardrails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "blocked_topics",
                table: "ai_agents",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "out_of_scope_reply",
                table: "ai_agents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                // Not "": every assistant that already exists gets the same sentence a new
                // one starts with. An empty reply would violate AiAgent's own invariant the
                // first time a blocked topic fired, and send a blank message to a customer.
                defaultValue: "Eso prefiero que te lo responda alguien del equipo. Escríbeles directamente y con gusto te ayudan.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "blocked_topics",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "out_of_scope_reply",
                table: "ai_agents");
        }
    }
}
