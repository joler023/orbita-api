using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPlansAndSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    included_conversations = table.Column<int>(type: "integer", nullable: false),
                    included_ai_credits = table.Column<int>(type: "integer", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    stripe_price_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_customer_id = table.Column<string>(type: "text", nullable: false),
                    provider_subscription_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_provider_provider_subscription_id",
                table: "subscriptions",
                columns: new[] { "provider", "provider_subscription_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_tenant_id",
                table: "subscriptions",
                column: "tenant_id",
                unique: true);

            // Seed catalog (ORB-A12's acceptance criterion: "catálogo de planes con
            // conversaciones incluidas, créditos de IA y límites"). Fixed ids so this
            // is idempotent and referenceable from tests/docs. stripe_price_id stays
            // null until a real Stripe account exists — see Plan.SetStripePriceId.
            migrationBuilder.InsertData(
                table: "plans",
                columns: new[] { "id", "code", "name", "included_conversations", "included_ai_credits", "price_amount", "price_currency", "is_active", "created_at" },
                values: new object[,]
                {
                    { new Guid("9c8e6f5a-1b2c-4d3e-8f9a-0b1c2d3e4f50"), "starter", "Starter", 500, 1000, 29.00m, "USD", true, new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero) },
                    { new Guid("9c8e6f5a-1b2c-4d3e-8f9a-0b1c2d3e4f51"), "growth", "Growth", 2000, 5000, 79.00m, "USD", true, new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero) },
                    { new Guid("9c8e6f5a-1b2c-4d3e-8f9a-0b1c2d3e4f52"), "scale", "Scale", 8000, 20000, 199.00m, "USD", true, new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero) },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "subscriptions");
        }
    }
}
