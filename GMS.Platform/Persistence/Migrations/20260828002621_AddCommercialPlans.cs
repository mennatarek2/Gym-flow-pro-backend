using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commercial_plans",
                schema: "platform",
                columns: table => new
                {
                    Tier = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActiveForSales = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    MonthlyPriceEgp = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_plans", x => x.Tier);
                    table.CheckConstraint("CK_commercial_plans_tier", "[Tier] IN ('starter','growth','pro','enterprise')");
                });

            migrationBuilder.CreateTable(
                name: "plan_change_log",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_change_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_commercial_plans_single_default",
                schema: "platform",
                table: "commercial_plans",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_plan_change_log_Tier_CreatedAtUtc",
                schema: "platform",
                table: "plan_change_log",
                columns: new[] { "Tier", "CreatedAtUtc" });

            var seededAt = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                schema: "platform",
                table: "commercial_plans",
                columns: new[] { "Tier", "DisplayName", "Description", "SortOrder", "IsActiveForSales", "IsDefault", "MonthlyPriceEgp", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { "starter", "Starter", "Small gyms getting started.", 1, true, false, 999m, seededAt },
                    { "growth", "Growth", "Mid-market gyms with imports and HR.", 2, true, true, 1999m, seededAt },
                    { "pro", "Pro", "Multi-branch operators with full inventory.", 3, true, false, 3999m, seededAt },
                    { "enterprise", "Enterprise", "Unlimited scale and full module access.", 4, true, false, 7999m, seededAt }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commercial_plans",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "plan_change_log",
                schema: "platform");
        }
    }
}
