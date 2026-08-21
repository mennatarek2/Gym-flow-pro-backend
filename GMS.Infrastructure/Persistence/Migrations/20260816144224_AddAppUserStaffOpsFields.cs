using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUserStaffOpsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "app_users",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "HireDate",
                table: "app_users",
                type: "DATE",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "app_users",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "app_users",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaffNumber",
                table: "app_users",
                type: "VARCHAR(12)",
                maxLength: 12,
                nullable: true);

            // Existing staff (including Owner) get unique numbers so the filtered unique
            // index can apply and AllocateStaffNumberAsync never reuses a retired number.
            migrationBuilder.Sql("""
                ;WITH numbered AS (
                  SELECT Id, ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY CreatedAtUtc, Id) AS n
                  FROM app_users
                  WHERE StaffNumber IS NULL
                )
                UPDATE a
                SET StaffNumber = 'ST-' + RIGHT('0000' + CAST(n AS varchar(10)), 4)
                FROM app_users a
                INNER JOIN numbered x ON a.Id = x.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_TenantId_ActorUserId_CreatedAtUtc",
                table: "audit_events",
                columns: new[] { "TenantId", "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_app_users_TenantId_Department",
                table: "app_users",
                columns: new[] { "TenantId", "Department" });

            migrationBuilder.CreateIndex(
                name: "IX_app_users_TenantId_StaffNumber",
                table: "app_users",
                columns: new[] { "TenantId", "StaffNumber" },
                unique: true,
                filter: "[StaffNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_TenantId_ActorUserId_CreatedAtUtc",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_app_users_TenantId_Department",
                table: "app_users");

            migrationBuilder.DropIndex(
                name: "IX_app_users_TenantId_StaffNumber",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "HireDate",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "StaffNumber",
                table: "app_users");
        }
    }
}
