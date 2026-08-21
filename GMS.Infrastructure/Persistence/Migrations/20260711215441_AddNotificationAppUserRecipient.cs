using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationAppUserRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "MemberId",
                table: "notifications",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "AppUserId",
                table: "notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_AppUserId",
                table: "notifications",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_AppUserId",
                table: "notifications",
                columns: new[] { "TenantId", "AppUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_app_users_AppUserId",
                table: "notifications",
                column: "AppUserId",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_app_users_AppUserId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_AppUserId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_AppUserId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "MemberId",
                table: "notifications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
