using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHrAttendanceEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR(80)", maxLength: 80, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TIME", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TIME", nullable: false),
                    BreakMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_shifts_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_schedule_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "DATE", nullable: false),
                    Notes = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_schedule_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_schedule_assignments_employee_shifts_EmployeeShiftId",
                        column: x => x.EmployeeShiftId,
                        principalTable: "employee_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_schedule_assignments_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttendanceDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    CheckInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckOutAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkedMinutes = table.Column<int>(type: "int", nullable: false),
                    LateMinutes = table.Column<int>(type: "int", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_attendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_attendances_employee_schedule_assignments_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "employee_schedule_assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_attendances_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_attendances_EmployeeId",
                table: "employee_attendances",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_attendances_ScheduleId",
                table: "employee_attendances",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_attendances_TenantId_AttendanceDate",
                table: "employee_attendances",
                columns: new[] { "TenantId", "AttendanceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_attendances_TenantId_EmployeeId_AttendanceDate",
                table: "employee_attendances",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_schedule_assignments_EmployeeId",
                table: "employee_schedule_assignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_schedule_assignments_EmployeeShiftId",
                table: "employee_schedule_assignments",
                column: "EmployeeShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_schedule_assignments_TenantId_Date",
                table: "employee_schedule_assignments",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_schedule_assignments_TenantId_EmployeeId_Date",
                table: "employee_schedule_assignments",
                columns: new[] { "TenantId", "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_shifts_TenantId_Name",
                table: "employee_shifts",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_attendances");

            migrationBuilder.DropTable(
                name: "employee_schedule_assignments");

            migrationBuilder.DropTable(
                name: "employee_shifts");
        }
    }
}
