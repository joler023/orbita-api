using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAgentForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_conversations_ai_agent_id",
                table: "conversations",
                column: "ai_agent_id");

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_ai_agents_ai_agent_id",
                table: "conversations",
                column: "ai_agent_id",
                principalTable: "ai_agents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_ai_agents_ai_agent_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_ai_agent_id",
                table: "conversations");
        }
    }
}
