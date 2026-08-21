using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftCashDrawer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    OpeningFloat = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    CountedCash = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    Variance = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    VarianceNote = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false, defaultValue: "open"),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shifts_app_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shifts_app_users_UserId",
                        column: x => x.UserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shifts_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "VARCHAR(12)", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_movements_app_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_movements_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cash_movements_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_ShiftId",
                table: "sales",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_ShiftId",
                table: "payment_transactions",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_CreatedByUserId",
                table: "cash_movements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_ShiftId",
                table: "cash_movements",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_TenantId_ShiftId",
                table: "cash_movements",
                columns: new[] { "TenantId", "ShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_shifts_ApprovedByUserId",
                table: "shifts",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_shifts_TenantId_Status",
                table: "shifts",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_shifts_TenantId_UserId",
                table: "shifts",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "[Status] = 'open'");

            migrationBuilder.CreateIndex(
                name: "IX_shifts_UserId",
                table: "shifts",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_transactions_shifts_ShiftId",
                table: "payment_transactions",
                column: "ShiftId",
                principalTable: "shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_shifts_ShiftId",
                table: "sales",
                column: "ShiftId",
                principalTable: "shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_transactions_shifts_ShiftId",
                table: "payment_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_shifts_ShiftId",
                table: "sales");

            migrationBuilder.DropTable(
                name: "cash_movements");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropIndex(
                name: "IX_sales_ShiftId",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_ShiftId",
                table: "payment_transactions");
        }
    }
}
