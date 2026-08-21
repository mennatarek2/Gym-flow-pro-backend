using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberOrdersAndVisibleToMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VisibleToMembers",
                table: "products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "member_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "VARCHAR(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "CHAR(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    Subtotal = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Total = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    MemberNotes = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    RejectionReason = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReadyByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_member_orders_app_users_AcceptedByUserId",
                        column: x => x.AcceptedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_orders_app_users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_orders_app_users_ReadyByUserId",
                        column: x => x.ReadyByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_orders_app_users_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_orders_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_orders_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_orders_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "member_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductSku = table.Column<string>(type: "VARCHAR(64)", maxLength: 64, nullable: false),
                    ProductName = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: false),
                    ProductNameAr = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Qty = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "CHAR(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_member_order_lines_member_orders_MemberOrderId",
                        column: x => x.MemberOrderId,
                        principalTable: "member_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_member_order_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_order_lines_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_VisibleToMembers_IsActive",
                table: "products",
                columns: new[] { "TenantId", "VisibleToMembers", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_member_order_lines_MemberOrderId",
                table: "member_order_lines",
                column: "MemberOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_member_order_lines_ProductId",
                table: "member_order_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_member_order_lines_TenantId_MemberOrderId",
                table: "member_order_lines",
                columns: new[] { "TenantId", "MemberOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_AcceptedByUserId",
                table: "member_orders",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_CompletedByUserId",
                table: "member_orders",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_MemberId",
                table: "member_orders",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_ReadyByUserId",
                table: "member_orders",
                column: "ReadyByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_RejectedByUserId",
                table: "member_orders",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_TenantId_MemberId_CreatedAtUtc",
                table: "member_orders",
                columns: new[] { "TenantId", "MemberId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_TenantId_OrderNumber",
                table: "member_orders",
                columns: new[] { "TenantId", "OrderNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_TenantId_Status_CreatedAtUtc",
                table: "member_orders",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_member_orders_WarehouseId",
                table: "member_orders",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_order_lines");

            migrationBuilder.DropTable(
                name: "member_orders");

            migrationBuilder.DropIndex(
                name: "IX_products_TenantId_VisibleToMembers_IsActive",
                table: "products");

            migrationBuilder.DropColumn(
                name: "VisibleToMembers",
                table: "products");
        }
    }
}
