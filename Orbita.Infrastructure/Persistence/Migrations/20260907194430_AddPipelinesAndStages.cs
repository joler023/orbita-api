using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelinesAndStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipelines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipelines", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipelines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_won = table.Column<bool>(type: "boolean", nullable: false),
                    is_lost = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_stages", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipeline_stages_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pipeline_stages_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunities", x => x.id);
                    table.ForeignKey(
                        name: "FK_opportunities_pipeline_stages_stage_id",
                        column: x => x.stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunities_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunities_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_pipeline_id",
                table: "opportunities",
                column: "pipeline_id");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_stage_id",
                table: "opportunities",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_tenant_pipeline",
                table: "opportunities",
                columns: new[] { "tenant_id", "pipeline_id" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_tenant_stage",
                table: "opportunities",
                columns: new[] { "tenant_id", "stage_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_pipeline_id",
                table: "pipeline_stages",
                column: "pipeline_id");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_tenant_pipeline_order",
                table: "pipeline_stages",
                columns: new[] { "tenant_id", "pipeline_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_pipelines_tenant_default",
                table: "pipelines",
                columns: new[] { "tenant_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_pipelines_tenant_name",
                table: "pipelines",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.Sql(
                """
                ALTER TABLE pipelines ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON pipelines
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE pipeline_stages ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON pipeline_stages
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE opportunities ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON opportunities
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);

            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                INSERT INTO pipelines (id, tenant_id, name, is_default, created_at, updated_at)
                SELECT gen_random_uuid(), t.id, 'Ventas', true, now(), now()
                FROM tenants t
                WHERE NOT EXISTS (SELECT 1 FROM pipelines p WHERE p.tenant_id = t.id);

                INSERT INTO pipeline_stages (id, tenant_id, pipeline_id, name, sort_order, is_won, is_lost, created_at, updated_at)
                SELECT gen_random_uuid(), p.tenant_id, p.id, stage.name, stage.sort_order, stage.is_won, stage.is_lost, now(), now()
                FROM pipelines p
                CROSS JOIN (VALUES
                    ('Nuevo', 0, false, false),
                    ('En conversación', 1, false, false),
                    ('Propuesta', 2, false, false),
                    ('Ganada', 3, true, false),
                    ('Perdida', 4, false, true)
                ) AS stage(name, sort_order, is_won, is_lost)
                WHERE NOT EXISTS (SELECT 1 FROM pipeline_stages s WHERE s.pipeline_id = p.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON opportunities;
                ALTER TABLE opportunities DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON pipeline_stages;
                ALTER TABLE pipeline_stages DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON pipelines;
                ALTER TABLE pipelines DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "opportunities");

            migrationBuilder.DropTable(
                name: "pipeline_stages");

            migrationBuilder.DropTable(
                name: "pipelines");
        }
    }
}
