using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationDunningCp5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes");

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAtUtc",
                schema: "platform",
                table: "subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "automation_enrollments",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Step = table.Column<int>(type: "int", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HaltedReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    HaltedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_enrollments", x => x.Id);
                    table.CheckConstraint("CK_automation_enrollments_step", "[Step] >= 0");
                    table.CheckConstraint("CK_automation_enrollments_subject_type", "[SubjectType] IN ('member','platform_invoice')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes",
                sql: "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','trial_start','past_due','suspension')");

            migrationBuilder.CreateIndex(
                name: "IX_automation_enrollments_due",
                schema: "platform",
                table: "automation_enrollments",
                columns: new[] { "NextRunAtUtc", "HaltedReason" });

            migrationBuilder.CreateIndex(
                name: "UX_automation_enrollments_active_subject",
                schema: "platform",
                table: "automation_enrollments",
                columns: new[] { "SequenceKey", "SubjectType", "SubjectId" },
                unique: true,
                filter: "[HaltedReason] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_enrollments",
                schema: "platform");

            migrationBuilder.DropCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                schema: "platform",
                table: "subscriptions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes",
                sql: "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','trial_start')");
        }
    }
}
