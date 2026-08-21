using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryProductCatalogInvs1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_categories_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sku = table.Column<string>(type: "VARCHAR(64)", maxLength: 64, nullable: false),
                    Barcode = table.Column<string>(type: "VARCHAR(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    DescriptionAr = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    Brand = table.Column<string>(type: "NVARCHAR(100)", maxLength: 100, nullable: true),
                    ImageUrl = table.Column<string>(type: "VARCHAR(500)", maxLength: 500, nullable: true),
                    UnitOfMeasure = table.Column<string>(type: "VARCHAR(16)", maxLength: 16, nullable: false, defaultValue: "pcs"),
                    SellPrice = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    CostPrice = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false, defaultValue: 0m),
                    Currency = table.Column<string>(type: "CHAR(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    Taxable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    VatRatePercent = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: true),
                    TrackStock = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    TrackBatch = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    TrackExpiry = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowFractionalQty = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsSellable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsPurchasable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ReorderMinQty = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false, defaultValue: 0m),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_products_product_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "product_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_TenantId",
                table: "product_categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_products_CategoryId",
                table: "products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Barcode",
                table: "products",
                columns: new[] { "TenantId", "Barcode" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_IsArchived_IsActive",
                table: "products",
                columns: new[] { "TenantId", "IsArchived", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Sku",
                table: "products",
                columns: new[] { "TenantId", "Sku" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "product_categories");
        }
    }
}
