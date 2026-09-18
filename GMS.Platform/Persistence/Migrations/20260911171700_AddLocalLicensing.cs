using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_activation_attempts",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LicenseKeyAttempted = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InstallationId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_activation_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "local_licenses",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerContact = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DealReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Product = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Edition = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeviceLimit = table.Column<int>(type: "int", nullable: false),
                    IssuedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LicenseVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_licenses", x => x.Id);
                    table.CheckConstraint("CK_local_licenses_device_limit", "[DeviceLimit] > 0");
                    table.CheckConstraint("CK_local_licenses_status", "[Status] IN ('created','pending_activation','active','suspended','revoked')");
                });

            migrationBuilder.CreateTable(
                name: "local_installations",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    MachineFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FirstActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeactivationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_installations", x => x.Id);
                    table.CheckConstraint("CK_local_installations_status", "[Status] IN ('active','deactivated')");
                    table.ForeignKey(
                        name: "FK_local_installations_local_licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalSchema: "platform",
                        principalTable: "local_licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_license_changes",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    InitiatedBy = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    PlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_license_changes", x => x.Id);
                    table.CheckConstraint("CK_local_license_changes_initiated_by", "[InitiatedBy] IN ('platform_admin','system','customer')");
                    table.ForeignKey(
                        name: "FK_local_license_changes_local_licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalSchema: "platform",
                        principalTable: "local_licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_activation_attempts_CreatedAtUtc",
                schema: "platform",
                table: "local_activation_attempts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_local_activation_attempts_LicenseId",
                schema: "platform",
                table: "local_activation_attempts",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_local_activation_attempts_LicenseKeyAttempted",
                schema: "platform",
                table: "local_activation_attempts",
                column: "LicenseKeyAttempted");

            migrationBuilder.CreateIndex(
                name: "IX_local_activation_attempts_Result",
                schema: "platform",
                table: "local_activation_attempts",
                column: "Result");

            migrationBuilder.CreateIndex(
                name: "IX_local_installations_InstallationId",
                schema: "platform",
                table: "local_installations",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_local_installations_LicenseId_InstallationId",
                schema: "platform",
                table: "local_installations",
                columns: new[] { "LicenseId", "InstallationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_installations_Status",
                schema: "platform",
                table: "local_installations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_local_license_changes_CreatedAtUtc",
                schema: "platform",
                table: "local_license_changes",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_local_license_changes_LicenseId",
                schema: "platform",
                table: "local_license_changes",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_local_licenses_CustomerName",
                schema: "platform",
                table: "local_licenses",
                column: "CustomerName");

            migrationBuilder.CreateIndex(
                name: "IX_local_licenses_LicenseKey",
                schema: "platform",
                table: "local_licenses",
                column: "LicenseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_licenses_Status",
                schema: "platform",
                table: "local_licenses",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_activation_attempts",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "local_installations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "local_license_changes",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "local_licenses",
                schema: "platform");
        }
    }
}
