using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAppActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeAppUserId",
                table: "employees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_app_activation_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_app_activation_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_app_activation_codes_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_app_activation_codes_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employees_EmployeeAppUserId",
                table: "employees",
                column: "EmployeeAppUserId",
                unique: true,
                filter: "[EmployeeAppUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_app_activation_codes_EmployeeId",
                table: "employee_app_activation_codes",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_app_activation_codes_TenantId_CodeHash",
                table: "employee_app_activation_codes",
                columns: new[] { "TenantId", "CodeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_app_activation_codes_TenantId_EmployeeId_ConsumedAtUtc_RevokedAtUtc",
                table: "employee_app_activation_codes",
                columns: new[] { "TenantId", "EmployeeId", "ConsumedAtUtc", "RevokedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_employees_app_users_EmployeeAppUserId",
                table: "employees",
                column: "EmployeeAppUserId",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employees_app_users_EmployeeAppUserId",
                table: "employees");

            migrationBuilder.DropTable(
                name: "employee_app_activation_codes");

            migrationBuilder.DropIndex(
                name: "IX_employees_EmployeeAppUserId",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "EmployeeAppUserId",
                table: "employees");
        }
    }
}
