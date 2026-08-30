using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffNotificationInboxFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionUrl",
                table: "notifications",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "notifications",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EntityId",
                table: "notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "notifications",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "notifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "notifications",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_AppUserId_CreatedAtUtc",
                table: "notifications",
                columns: new[] { "TenantId", "AppUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_AppUserId_ReadAtUtc",
                table: "notifications",
                columns: new[] { "TenantId", "AppUserId", "ReadAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_Category",
                table: "notifications",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_ExternalMessageId",
                table: "notifications",
                columns: new[] { "TenantId", "ExternalMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_AppUserId_CreatedAtUtc",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_AppUserId_ReadAtUtc",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_Category",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_ExternalMessageId",
                table: "notifications");

            migrationBuilder.DropColumn(name: "ActionUrl", table: "notifications");
            migrationBuilder.DropColumn(name: "Category", table: "notifications");
            migrationBuilder.DropColumn(name: "EntityId", table: "notifications");
            migrationBuilder.DropColumn(name: "EntityType", table: "notifications");
            migrationBuilder.DropColumn(name: "ExpiresAtUtc", table: "notifications");
            migrationBuilder.DropColumn(name: "Priority", table: "notifications");
            migrationBuilder.DropColumn(name: "Type", table: "notifications");
        }
    }
}
