using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryG4AdjustmentBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "stock_adjustment_lines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustment_lines_BatchId",
                table: "stock_adjustment_lines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustment_lines_TenantId_BatchId",
                table: "stock_adjustment_lines",
                columns: new[] { "TenantId", "BatchId" });

            migrationBuilder.AddForeignKey(
                name: "FK_stock_adjustment_lines_product_batches_BatchId",
                table: "stock_adjustment_lines",
                column: "BatchId",
                principalTable: "product_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_adjustment_lines_product_batches_BatchId",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "IX_stock_adjustment_lines_BatchId",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "IX_stock_adjustment_lines_TenantId_BatchId",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "stock_adjustment_lines");
        }
    }
}
