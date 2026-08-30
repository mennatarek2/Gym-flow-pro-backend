using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowSubscriptionCancelUndoChangeType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes",
                sql: "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','cancel_undo','trial_start','trial_extend','past_due','suspension')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_subscription_changes_change_type",
                schema: "platform",
                table: "subscription_changes",
                sql: "[ChangeType] IN ('upgrade','downgrade','cycle_change','reactivation','cancellation','trial_start','trial_extend','past_due','suspension')");
        }
    }
}
