using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpportunityBoardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to_user_id",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_move_event_id",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_tenant_assignee",
                table: "opportunities",
                columns: new[] { "tenant_id", "assigned_to_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_opportunities_tenant_assignee",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "assigned_to_user_id",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "last_move_event_id",
                table: "opportunities");
        }
    }
}
