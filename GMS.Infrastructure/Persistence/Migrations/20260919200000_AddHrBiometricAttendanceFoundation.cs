using System;
using GMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Generic biometric attendance foundation: devices, employee mappings, raw events.
    /// Hand-authored because GMS.Api DLL locks blocked <c>dotnet ef migrations add</c> on the
    /// authoring machine; model matches HrBiometricConfigurations. Snapshot reconcile recommended
    /// when the API process is stopped: <c>dotnet ef migrations add …</c> (expect empty if already applied).
    /// </summary>
    [DbContext(typeof(GymFlowProDbContext))]
    [Migration("20260919200000_AddHrBiometricAttendanceFoundation")]
    public partial class AddHrBiometricAttendanceFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "biometric_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceCode = table.Column<string>(type: "NVARCHAR(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "NVARCHAR(120)", maxLength: 120, nullable: false),
                    Vendor = table.Column<string>(type: "NVARCHAR(80)", maxLength: 80, nullable: true),
                    Model = table.Column<string>(type: "NVARCHAR(80)", maxLength: 80, nullable: true),
                    IntegrationType = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: false),
                    LocationLabel = table.Column<string>(type: "NVARCHAR(120)", maxLength: 120, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    HealthStatus = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: false),
                    LastSuccessfulSyncAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    SafeConfigJson = table.Column<string>(type: "NVARCHAR(4000)", maxLength: 4000, nullable: true),
                    ApiKeyHash = table.Column<string>(type: "VARCHAR(64)", maxLength: 64, nullable: false),
                    ApiKeyPrefix = table.Column<string>(type: "VARCHAR(16)", maxLength: 16, nullable: false),
                    ApiKeyRotatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_biometric_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_biometric_devices_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "biometric_employee_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BiometricDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceUserId = table.Column<string>(type: "NVARCHAR(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DisabledReason = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: true),
                    DisabledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemoteDisableStatus = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: false),
                    RemoteDisableRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemoteDisableConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemoteDisableNote = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_biometric_employee_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_biometric_employee_mappings_biometric_devices_BiometricDeviceId",
                        column: x => x.BiometricDeviceId,
                        principalTable: "biometric_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_biometric_employee_mappings_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "biometric_attendance_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BiometricDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorEventId = table.Column<string>(type: "NVARCHAR(120)", maxLength: 120, nullable: true),
                    DeviceUserId = table.Column<string>(type: "NVARCHAR(64)", maxLength: 64, nullable: false),
                    DeviceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OriginalDeviceTimeText = table.Column<string>(type: "NVARCHAR(64)", maxLength: 64, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PunchDirection = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    ProcessingStatus = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    ReviewReason = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    ValidationResult = table.Column<string>(type: "VARCHAR(80)", maxLength: 80, nullable: true),
                    ResolvedEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultingAttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClockSkewSeconds = table.Column<int>(type: "int", nullable: true),
                    SafePayloadJson = table.Column<string>(type: "NVARCHAR(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_biometric_attendance_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_biometric_attendance_events_biometric_devices_BiometricDeviceId",
                        column: x => x.BiometricDeviceId,
                        principalTable: "biometric_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_biometric_attendance_events_employee_attendances_ResultingAttendanceId",
                        column: x => x.ResultingAttendanceId,
                        principalTable: "employee_attendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_biometric_attendance_events_employees_ResolvedEmployeeId",
                        column: x => x.ResolvedEmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_biometric_devices_TenantId",
                table: "biometric_devices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_devices_TenantId_ApiKeyHash",
                table: "biometric_devices",
                columns: new[] { "TenantId", "ApiKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_biometric_devices_TenantId_DeviceCode",
                table: "biometric_devices",
                columns: new[] { "TenantId", "DeviceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_biometric_employee_mappings_TenantId_BiometricDeviceId_DeviceUserId",
                table: "biometric_employee_mappings",
                columns: new[] { "TenantId", "BiometricDeviceId", "DeviceUserId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [IsEnabled] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_employee_mappings_TenantId_BiometricDeviceId_EmployeeId",
                table: "biometric_employee_mappings",
                columns: new[] { "TenantId", "BiometricDeviceId", "EmployeeId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [IsEnabled] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_employee_mappings_TenantId_EmployeeId",
                table: "biometric_employee_mappings",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_biometric_employee_mappings_BiometricDeviceId",
                table: "biometric_employee_mappings",
                column: "BiometricDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_employee_mappings_EmployeeId",
                table: "biometric_employee_mappings",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_attendance_events_TenantId_BiometricDeviceId_VendorEventId",
                table: "biometric_attendance_events",
                columns: new[] { "TenantId", "BiometricDeviceId", "VendorEventId" },
                unique: true,
                filter: "[VendorEventId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_attendance_events_TenantId_BiometricDeviceId_DeviceUserId_DeviceTimestampUtc",
                table: "biometric_attendance_events",
                columns: new[] { "TenantId", "BiometricDeviceId", "DeviceUserId", "DeviceTimestampUtc" },
                unique: true,
                filter: "[VendorEventId] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_attendance_events_TenantId_ProcessingStatus_ReceivedAtUtc",
                table: "biometric_attendance_events",
                columns: new[] { "TenantId", "ProcessingStatus", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_biometric_attendance_events_TenantId_ResolvedEmployeeId_DeviceTimestampUtc",
                table: "biometric_attendance_events",
                columns: new[] { "TenantId", "ResolvedEmployeeId", "DeviceTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_biometric_attendance_events_BiometricDeviceId",
                table: "biometric_attendance_events",
                column: "BiometricDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_attendance_events_ResolvedEmployeeId",
                table: "biometric_attendance_events",
                column: "ResolvedEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_biometric_attendance_events_ResultingAttendanceId",
                table: "biometric_attendance_events",
                column: "ResultingAttendanceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "biometric_attendance_events");
            migrationBuilder.DropTable(name: "biometric_employee_mappings");
            migrationBuilder.DropTable(name: "biometric_devices");
        }
    }
}
