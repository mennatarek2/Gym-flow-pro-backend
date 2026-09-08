using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQrAttendanceHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AttendanceDateCairo",
                table: "gym_attendance",
                type: "DATE",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Backfill existing rows from CheckInAtUtc (same Cairo-day convention as
            // GMS.Core.Utilities.MembershipOperational.ToCairoDate — "Egypt Standard Time",
            // not a placeholder). New rows get this stamped on insert by GymFlowProDbContext.
            migrationBuilder.Sql(
                "UPDATE gym_attendance " +
                "SET AttendanceDateCairo = CAST(CheckInAtUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time' AS DATE);");

            // NOTE: if this tenant's data already has more than one non-session (SessionId IS NULL)
            // gym-floor check-in for the same member on the same Cairo day — a pre-existing gap this
            // migration is specifically closing — creating the unique index below will fail with a
            // duplicate-key error. That is a real, existing data-integrity issue to investigate and
            // clean up (e.g. soft-delete the older duplicate) before re-running this migration; do not
            // work around it by widening or dropping the filter.
            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_TenantId_MemberId_AttendanceDateCairo_Unique",
                table: "gym_attendance",
                columns: new[] { "TenantId", "MemberId", "AttendanceDateCairo" },
                unique: true,
                filter: "[MemberId] IS NOT NULL AND [SessionId] IS NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gym_attendance_TenantId_MemberId_AttendanceDateCairo_Unique",
                table: "gym_attendance");

            migrationBuilder.DropColumn(
                name: "AttendanceDateCairo",
                table: "gym_attendance");
        }
    }
}
