using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaleAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sale_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false),
                    Type = table.Column<string>(type: "VARCHAR(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sale_adjustments_app_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_adjustments_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_adjustments_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_adjustments_CreatedByUserId",
                table: "sale_adjustments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_adjustments_SaleId",
                table: "sale_adjustments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_adjustments_TenantId_SaleId_Status",
                table: "sale_adjustments",
                columns: new[] { "TenantId", "SaleId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_adjustments");
        }
    }
}
