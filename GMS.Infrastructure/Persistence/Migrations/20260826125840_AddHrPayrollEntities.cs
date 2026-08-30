using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHrPayrollEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_periods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_periods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payroll_periods_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false),
                    Reason = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payroll_adjustments_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payroll_adjustments_payroll_periods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "payroll_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payroll_adjustments_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payroll_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BasicSalary = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false),
                    OvertimeAmount = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false, defaultValue: 0m),
                    BonusAmount = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false, defaultValue: 0m),
                    AllowanceAmount = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false, defaultValue: 0m),
                    DeductionAmount = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false, defaultValue: 0m),
                    NetSalary = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payroll_lines_employee_contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "employee_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payroll_lines_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payroll_lines_payroll_periods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "payroll_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payroll_lines_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payroll_adjustments_EmployeeId",
                table: "payroll_adjustments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_adjustments_PayrollPeriodId",
                table: "payroll_adjustments",
                column: "PayrollPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_adjustments_TenantId_PayrollPeriodId_EmployeeId",
                table: "payroll_adjustments",
                columns: new[] { "TenantId", "PayrollPeriodId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_payroll_lines_ContractId",
                table: "payroll_lines",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_lines_EmployeeId",
                table: "payroll_lines",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_lines_PayrollPeriodId",
                table: "payroll_lines",
                column: "PayrollPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_lines_TenantId_EmployeeId",
                table: "payroll_lines",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_payroll_lines_TenantId_PayrollPeriodId_EmployeeId",
                table: "payroll_lines",
                columns: new[] { "TenantId", "PayrollPeriodId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payroll_periods_TenantId_Year_Month",
                table: "payroll_periods",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_adjustments");

            migrationBuilder.DropTable(
                name: "payroll_lines");

            migrationBuilder.DropTable(
                name: "payroll_periods");
        }
    }
}
