using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalOwnerRecoveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_owner_recoveries",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InstallationId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GymCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GymName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Nonce = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionByPlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovalJti = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalBlob = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HistoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_owner_recoveries", x => x.Id);
                    table.CheckConstraint("CK_local_owner_recoveries_method", "[Method] IN ('online','offline')");
                    table.CheckConstraint("CK_local_owner_recoveries_status", "[Status] IN ('pending','approved','completed','rejected','expired','cancelled','revoked')");
                    table.ForeignKey(
                        name: "FK_local_owner_recoveries_local_licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalSchema: "platform",
                        principalTable: "local_licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_owner_recoveries_ApprovalJti",
                schema: "platform",
                table: "local_owner_recoveries",
                column: "ApprovalJti",
                unique: true,
                filter: "[ApprovalJti] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_owner_recoveries_CreatedAtUtc",
                schema: "platform",
                table: "local_owner_recoveries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_local_owner_recoveries_CustomerId",
                schema: "platform",
                table: "local_owner_recoveries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_local_owner_recoveries_InstallationId",
                schema: "platform",
                table: "local_owner_recoveries",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_local_owner_recoveries_LicenseId",
                schema: "platform",
                table: "local_owner_recoveries",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_local_owner_recoveries_Status",
                schema: "platform",
                table: "local_owner_recoveries",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_owner_recoveries",
                schema: "platform");
        }
    }
}
