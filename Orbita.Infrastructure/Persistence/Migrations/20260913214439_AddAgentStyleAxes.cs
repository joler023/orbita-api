using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ORB-C10. One <c>tone</c> becomes three axes, because it conflated things owners want
    /// to set separately — a formal assistant is not necessarily a terse one, and a warm
    /// one is not necessarily an excitable one.
    /// </summary>
    public partial class AddAgentStyleAxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // tone's three values were Formal/Balanced/Conversational — a formality scale,
            // so that is the axis it becomes. Renaming rather than dropping keeps whatever
            // owners had already configured; only the third level is spelled differently
            // (Conversational -> Warm), so it is translated rather than left to fail the
            // enum conversion on the next read.
            migrationBuilder.RenameColumn(
                name: "tone",
                table: "ai_agents",
                newName: "formality");

            migrationBuilder.Sql(
                "UPDATE ai_agents SET formality = 'Warm' WHERE formality = 'Conversational';");

            // Balanced, not '': an empty string is not a level, and the enum conversion
            // would throw on the first read of a row that predates this migration.
            migrationBuilder.AddColumn<string>(
                name: "verbosity",
                table: "ai_agents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Balanced");

            migrationBuilder.AddColumn<string>(
                name: "energy",
                table: "ai_agents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Balanced");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "energy",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "verbosity",
                table: "ai_agents");

            // Going back loses the two extra axes, so Warm folds back into the single
            // level it came from.
            migrationBuilder.Sql(
                "UPDATE ai_agents SET formality = 'Conversational' WHERE formality = 'Warm';");

            migrationBuilder.RenameColumn(
                name: "formality",
                table: "ai_agents",
                newName: "tone");
        }
    }
}
