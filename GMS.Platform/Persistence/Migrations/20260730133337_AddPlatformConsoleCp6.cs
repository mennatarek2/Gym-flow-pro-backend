using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformConsoleCp6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes");

            migrationBuilder.CreateTable(
                name: "price_overrides",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    GrantedByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_overrides", x => x.Id);
                    table.CheckConstraint("CK_price_overrides_discount_type", "[DiscountType] IN ('percent','fixed')");
                    table.CheckConstraint("CK_price_overrides_value", "[Value] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "tenant_health_scores",
                schema: "platform",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiskBand = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    BreakdownJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_health_scores", x => x.TenantId);
                    table.CheckConstraint("CK_tenant_health_scores_risk_band", "[RiskBand] IN ('green','amber','red')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes",
                sql: "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','trial_start','trial_extend','past_due','suspension')");

            migrationBuilder.CreateIndex(
                name: "IX_price_overrides_TenantId_ExpiresAtUtc",
                schema: "platform",
                table: "price_overrides",
                columns: new[] { "TenantId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_overrides",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_health_scores",
                schema: "platform");

            migrationBuilder.DropCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes",
                sql: "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','trial_start','past_due','suspension')");
        }
    }
}
