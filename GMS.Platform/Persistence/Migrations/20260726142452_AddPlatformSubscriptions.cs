using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanTier = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PriceEgp = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CurrentPeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentPeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.Id);
                    table.CheckConstraint("CK_subscriptions_billing_cycle", "[BillingCycle] IN ('monthly','annual')");
                    table.CheckConstraint("CK_subscriptions_plan_tier", "[PlanTier] IN ('starter','growth','pro','enterprise')");
                    table.CheckConstraint("CK_subscriptions_status", "[Status] IN ('trialing','active','past_due','suspended','cancelled')");
                    table.ForeignKey(
                        name: "FK_subscriptions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_changes",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    FromTier = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ToTier = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProratedAmountEgp = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    InitiatedBy = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    PlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_changes", x => x.Id);
                    table.CheckConstraint("CK_subscription_changes_change_type", "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','trial_start')");
                    table.CheckConstraint("CK_subscription_changes_initiated_by", "[InitiatedBy] IN ('self_serve','platform_admin','system')");
                    table.ForeignKey(
                        name: "FK_subscription_changes_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "platform",
                        principalTable: "subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_changes_CreatedAtUtc",
                schema: "platform",
                table: "subscription_changes",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_changes_EffectiveAtUtc",
                schema: "platform",
                table: "subscription_changes",
                column: "EffectiveAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_changes_SubscriptionId",
                schema: "platform",
                table: "subscription_changes",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_changes_TenantId",
                schema: "platform",
                table: "subscription_changes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_CurrentPeriodEnd",
                schema: "platform",
                table: "subscriptions",
                column: "CurrentPeriodEnd");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_Status",
                schema: "platform",
                table: "subscriptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_subscriptions_tenant_live",
                schema: "platform",
                table: "subscriptions",
                column: "TenantId",
                unique: true,
                filter: "[Status] IN ('trialing','active','past_due')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_changes",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "platform");
        }
    }
}
