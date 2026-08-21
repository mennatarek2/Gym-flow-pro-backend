using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivitiesFacilitiesBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "gym_attendance",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "EntryMethod",
                table: "gym_attendance",
                type: "VARCHAR(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "qr",
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldMaxLength: 10,
                oldDefaultValue: "qr");

            migrationBuilder.AddColumn<Guid>(
                name: "BookingId",
                table: "gym_attendance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "gym_attendance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR(120)", maxLength: 120, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: false),
                    DescriptionAr = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    SystemKey = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    BookingRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DefaultCapacity = table.Column<int>(type: "int", nullable: true),
                    DefaultDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    DropInPrice = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    VisibleToMembers = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_activities_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DaysOfWeek = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TIME", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TIME", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    CoachUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "DATE", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "DATE", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_activity_schedules_activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_schedules_app_users_CoachUserId",
                        column: x => x.CoachUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_activity_schedules_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_entitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessMode = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    QuotaLimit = table.Column<int>(type: "int", nullable: true),
                    QuotaPeriod = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_entitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plan_entitlements_activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_entitlements_membership_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "membership_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_entitlements_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    CoachUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_activity_sessions_activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_sessions_activity_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "activity_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_activity_sessions_app_users_CoachUserId",
                        column: x => x.CoachUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_activity_sessions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    CoveringMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CheckedInAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CheckedInByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_activity_bookings_activity_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "activity_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_bookings_app_users_CheckedInByUserId",
                        column: x => x.CheckedInByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_activity_bookings_gym_attendance_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "gym_attendance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_bookings_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_bookings_memberships_CoveringMembershipId",
                        column: x => x.CoveringMembershipId,
                        principalTable: "memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_bookings_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_bookings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_BookingId",
                table: "gym_attendance",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_SessionId",
                table: "gym_attendance",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_activities_TenantId_Kind_IsActive",
                table: "activities",
                columns: new[] { "TenantId", "Kind", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_activities_TenantId_SystemKey",
                table: "activities",
                columns: new[] { "TenantId", "SystemKey" },
                unique: true,
                filter: "[SystemKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_AttendanceId",
                table: "activity_bookings",
                column: "AttendanceId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_CheckedInByUserId",
                table: "activity_bookings",
                column: "CheckedInByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_CoveringMembershipId",
                table: "activity_bookings",
                column: "CoveringMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_MemberId",
                table: "activity_bookings",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_SaleId",
                table: "activity_bookings",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_SessionId",
                table: "activity_bookings",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_TenantId_MemberId_CreatedAtUtc",
                table: "activity_bookings",
                columns: new[] { "TenantId", "MemberId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_TenantId_SessionId_MemberId",
                table: "activity_bookings",
                columns: new[] { "TenantId", "SessionId", "MemberId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] <> 'cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_activity_schedules_ActivityId",
                table: "activity_schedules",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_schedules_CoachUserId",
                table: "activity_schedules",
                column: "CoachUserId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_schedules_TenantId_ActivityId_IsActive",
                table: "activity_schedules",
                columns: new[] { "TenantId", "ActivityId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_ActivityId",
                table: "activity_sessions",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_CoachUserId",
                table: "activity_sessions",
                column: "CoachUserId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_ScheduleId",
                table: "activity_sessions",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_TenantId_ActivityId_StartsAtUtc",
                table: "activity_sessions",
                columns: new[] { "TenantId", "ActivityId", "StartsAtUtc" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_TenantId_StartsAtUtc",
                table: "activity_sessions",
                columns: new[] { "TenantId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_entitlements_ActivityId",
                table: "plan_entitlements",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_plan_entitlements_PlanId_ActivityId",
                table: "plan_entitlements",
                columns: new[] { "PlanId", "ActivityId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_plan_entitlements_TenantId_ActivityId",
                table: "plan_entitlements",
                columns: new[] { "TenantId", "ActivityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_gym_attendance_activity_bookings_BookingId",
                table: "gym_attendance",
                column: "BookingId",
                principalTable: "activity_bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gym_attendance_activity_sessions_SessionId",
                table: "gym_attendance",
                column: "SessionId",
                principalTable: "activity_sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gym_attendance_activity_bookings_BookingId",
                table: "gym_attendance");

            migrationBuilder.DropForeignKey(
                name: "FK_gym_attendance_activity_sessions_SessionId",
                table: "gym_attendance");

            migrationBuilder.DropTable(
                name: "activity_bookings");

            migrationBuilder.DropTable(
                name: "plan_entitlements");

            migrationBuilder.DropTable(
                name: "activity_sessions");

            migrationBuilder.DropTable(
                name: "activity_schedules");

            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropIndex(
                name: "IX_gym_attendance_BookingId",
                table: "gym_attendance");

            migrationBuilder.DropIndex(
                name: "IX_gym_attendance_SessionId",
                table: "gym_attendance");

            migrationBuilder.DropColumn(
                name: "BookingId",
                table: "gym_attendance");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "gym_attendance");

            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "gym_attendance",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EntryMethod",
                table: "gym_attendance",
                type: "VARCHAR(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "qr",
                oldClrType: typeof(string),
                oldType: "VARCHAR(16)",
                oldMaxLength: 16,
                oldDefaultValue: "qr");
        }
    }
}
