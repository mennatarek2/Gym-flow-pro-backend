using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityBookingIdempotencyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The previous schema already had an index with this name but a weaker filter
            // ([Status] <> 'cancelled'). Replace it with the stricter seat-holding filter.
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_activity_bookings_TenantId_SessionId_MemberId] ON [activity_bookings];");

            migrationBuilder.DropIndex(
                name: "IX_activity_sessions_ScheduleId",
                table: "activity_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_ScheduleId_StartsAtUtc",
                table: "activity_sessions",
                columns: new[] { "ScheduleId", "StartsAtUtc" },
                unique: true,
                filter: "[ScheduleId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_TenantId_SessionId_MemberId",
                table: "activity_bookings",
                columns: new[] { "TenantId", "SessionId", "MemberId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] IN ('booked', 'checked_in', 'no_show')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_activity_sessions_ScheduleId_StartsAtUtc",
                table: "activity_sessions");

            migrationBuilder.DropIndex(
                name: "IX_activity_bookings_TenantId_SessionId_MemberId",
                table: "activity_bookings");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_ScheduleId",
                table: "activity_sessions",
                column: "ScheduleId");
        }
    }
}
