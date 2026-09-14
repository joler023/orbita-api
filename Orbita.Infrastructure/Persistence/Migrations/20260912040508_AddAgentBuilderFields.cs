using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentBuilderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "instructions",
                table: "ai_agents",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "personality",
                table: "ai_agents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            // "Balanced", not "": tone is an enum stored as its name, and an empty string
            // is not one of them. Any row that predates this column would otherwise load
            // as an invalid enum rather than as the sensible default.
            migrationBuilder.AddColumn<string>(
                name: "tone",
                table: "ai_agents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Balanced");

            migrationBuilder.AddColumn<string>(
                name: "tools",
                table: "ai_agents",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "instructions",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "personality",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "tone",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "tools",
                table: "ai_agents");
        }
    }
}
