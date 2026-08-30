using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations;

public partial class AddGuestWalkInClassBookings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "MemberId",
            table: "activity_bookings",
            type: "uniqueidentifier",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier");

        migrationBuilder.AlterColumn<Guid>(
            name: "MemberId",
            table: "gym_attendance",
            type: "uniqueidentifier",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier");

        migrationBuilder.AddColumn<string>(
            name: "GuestName",
            table: "activity_bookings",
            type: "NVARCHAR(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuestPhone",
            table: "activity_bookings",
            type: "NVARCHAR(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuestName",
            table: "gym_attendance",
            type: "NVARCHAR(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuestPhone",
            table: "gym_attendance",
            type: "NVARCHAR(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuestName",
            table: "sales",
            type: "NVARCHAR(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuestPhone",
            table: "sales",
            type: "NVARCHAR(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS [IX_activity_bookings_TenantId_SessionId_MemberId] ON [activity_bookings];");
        migrationBuilder.CreateIndex(
            name: "IX_activity_bookings_TenantId_SessionId_MemberId",
            table: "activity_bookings",
            columns: new[] { "TenantId", "SessionId", "MemberId" },
            unique: true,
            filter: "[IsDeleted] = 0 AND [MemberId] IS NOT NULL AND [Status] IN ('booked', 'checked_in', 'no_show')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS [IX_activity_bookings_TenantId_SessionId_MemberId] ON [activity_bookings];");
        migrationBuilder.CreateIndex(
            name: "IX_activity_bookings_TenantId_SessionId_MemberId",
            table: "activity_bookings",
            columns: new[] { "TenantId", "SessionId", "MemberId" },
            unique: true,
            filter: "[IsDeleted] = 0 AND [Status] IN ('booked', 'checked_in', 'no_show')");

        migrationBuilder.DropColumn(name: "GuestName", table: "activity_bookings");
        migrationBuilder.DropColumn(name: "GuestPhone", table: "activity_bookings");
        migrationBuilder.DropColumn(name: "GuestName", table: "gym_attendance");
        migrationBuilder.DropColumn(name: "GuestPhone", table: "gym_attendance");
        migrationBuilder.DropColumn(name: "GuestName", table: "sales");
        migrationBuilder.DropColumn(name: "GuestPhone", table: "sales");

        migrationBuilder.AlterColumn<Guid>(
            name: "MemberId",
            table: "activity_bookings",
            type: "uniqueidentifier",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "MemberId",
            table: "gym_attendance",
            type: "uniqueidentifier",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);
    }
}
