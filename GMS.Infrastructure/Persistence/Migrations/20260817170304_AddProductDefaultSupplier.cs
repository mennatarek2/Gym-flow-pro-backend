using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductDefaultSupplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultSupplierId",
                table: "products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_DefaultSupplierId",
                table: "products",
                column: "DefaultSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_DefaultSupplierId",
                table: "products",
                columns: new[] { "TenantId", "DefaultSupplierId" });

            migrationBuilder.AddForeignKey(
                name: "FK_products_suppliers_DefaultSupplierId",
                table: "products",
                column: "DefaultSupplierId",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_products_suppliers_DefaultSupplierId",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_DefaultSupplierId",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_TenantId_DefaultSupplierId",
                table: "products");

            migrationBuilder.DropColumn(
                name: "DefaultSupplierId",
                table: "products");
        }
    }
}
