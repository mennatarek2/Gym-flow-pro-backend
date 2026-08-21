using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageEnforcementCp4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LinesSnapshot",
                schema: "platform",
                table: "platform_invoices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "feature_overrides",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    GrantedByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_overrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tier_feature_map",
                schema: "platform",
                columns: table => new
                {
                    Tier = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CapValue = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_feature_map", x => new { x.Tier, x.FeatureKey });
                    table.CheckConstraint("CK_tier_feature_map_tier", "[Tier] IN ('starter','growth','pro','enterprise')");
                });

            migrationBuilder.CreateTable(
                name: "usage_counters",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Cap = table.Column<int>(type: "int", nullable: true),
                    OverageBilledEgp = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_counters", x => x.Id);
                    table.CheckConstraint("CK_usage_counters_metric", "[Metric] IN ('active_members','whatsapp_messages','staff_seats','branches')");
                    table.CheckConstraint("CK_usage_counters_period", "LEN([Period]) = 7 AND [Period] LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_feature_overrides_GrantedByPlatformUserId",
                schema: "platform",
                table: "feature_overrides",
                column: "GrantedByPlatformUserId");

            migrationBuilder.CreateIndex(
                name: "IX_feature_overrides_TenantId_FeatureKey_ExpiresAtUtc",
                schema: "platform",
                table: "feature_overrides",
                columns: new[] { "TenantId", "FeatureKey", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_usage_counters_tenant_period_metric",
                schema: "platform",
                table: "usage_counters",
                columns: new[] { "TenantId", "Period", "Metric" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_overrides",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tier_feature_map",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "usage_counters",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "LinesSnapshot",
                schema: "platform",
                table: "platform_invoices");
        }
    }
}
