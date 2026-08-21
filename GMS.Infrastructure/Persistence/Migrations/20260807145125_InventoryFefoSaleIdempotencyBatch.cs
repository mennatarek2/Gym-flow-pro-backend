using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryFefoSaleIdempotencyBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_stock_movements_Idempotency",
                table: "stock_movements");

            migrationBuilder.CreateIndex(
                name: "UX_stock_movements_Idempotency_NoBatch",
                table: "stock_movements",
                columns: new[] { "TenantId", "ReferenceType", "ReferenceId", "Reason" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ReferenceId] IS NOT NULL AND [ReferenceType] IS NOT NULL AND [BatchId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_stock_movements_Idempotency_WithBatch",
                table: "stock_movements",
                columns: new[] { "TenantId", "ReferenceType", "ReferenceId", "Reason", "BatchId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ReferenceId] IS NOT NULL AND [ReferenceType] IS NOT NULL AND [BatchId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_stock_movements_Idempotency_NoBatch",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "UX_stock_movements_Idempotency_WithBatch",
                table: "stock_movements");

            migrationBuilder.CreateIndex(
                name: "UX_stock_movements_Idempotency",
                table: "stock_movements",
                columns: new[] { "TenantId", "ReferenceType", "ReferenceId", "Reason" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ReferenceId] IS NOT NULL AND [ReferenceType] IS NOT NULL");
        }
    }
}
