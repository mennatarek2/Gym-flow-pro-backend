using System;
using GMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GymFlowProDbContext))]
    [Migration("20260905220000_AddMemberOrderSaleId")]
    public partial class AddMemberOrderSaleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "member_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_SaleId",
                table: "member_orders",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_TenantId_SaleId",
                table: "member_orders",
                columns: new[] { "TenantId", "SaleId" },
                unique: true,
                filter: "[SaleId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_member_orders_sales_SaleId",
                table: "member_orders",
                column: "SaleId",
                principalTable: "sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_member_orders_sales_SaleId",
                table: "member_orders");

            migrationBuilder.DropIndex(
                name: "IX_member_orders_SaleId",
                table: "member_orders");

            migrationBuilder.DropIndex(
                name: "IX_member_orders_TenantId_SaleId",
                table: "member_orders");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "member_orders");
        }
    }
}
