using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalLifecycleEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                schema: "platform",
                table: "local_installations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GymCode",
                schema: "platform",
                table: "local_installations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GymName",
                schema: "platform",
                table: "local_installations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "local_lifecycle_events",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InstallationId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GymCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    GymName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_lifecycle_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_lifecycle_events_CreatedAtUtc",
                schema: "platform",
                table: "local_lifecycle_events",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_local_lifecycle_events_IdempotencyKey",
                schema: "platform",
                table: "local_lifecycle_events",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_lifecycle_events_LicenseId",
                schema: "platform",
                table: "local_lifecycle_events",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_local_lifecycle_events_OperationId",
                schema: "platform",
                table: "local_lifecycle_events",
                column: "OperationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_lifecycle_events",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "AppVersion",
                schema: "platform",
                table: "local_installations");

            migrationBuilder.DropColumn(
                name: "GymCode",
                schema: "platform",
                table: "local_installations");

            migrationBuilder.DropColumn(
                name: "GymName",
                schema: "platform",
                table: "local_installations");
        }
    }
}
