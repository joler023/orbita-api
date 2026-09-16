using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRunToolsAndChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "retrieved_chunk_ids",
                table: "ai_runs",
                type: "uuid[]",
                nullable: false,
                // Existing runs predate retrieval tracking: an empty array says "none
                // recorded", and NOT NULL without a default would fail on every row already
                // in the ledger.
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<string>(
                name: "tools_called",
                table: "ai_runs",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retrieved_chunk_ids",
                table: "ai_runs");

            migrationBuilder.DropColumn(
                name: "tools_called",
                table: "ai_runs");
        }
    }
}
