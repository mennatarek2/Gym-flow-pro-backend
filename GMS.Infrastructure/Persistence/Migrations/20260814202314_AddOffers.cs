using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: true),
                    ShortDescription = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: false),
                    ShortDescriptionAr = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "NVARCHAR(2000)", maxLength: 2000, nullable: true),
                    BannerUrl = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    AppliesTo = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    PlanIdsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ProductIdsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    MembershipLabelsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ProductLabelsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    DiscountType = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    MaxDiscount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    BuyQty = table.Column<int>(type: "int", nullable: true),
                    GetQty = table.Column<int>(type: "int", nullable: true),
                    AllMembers = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    NewMembersOnly = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    MinPurchase = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    UsageLimit = table.Column<int>(type: "int", nullable: true),
                    PerMemberLimit = table.Column<int>(type: "int", nullable: true),
                    UsesCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ShowOnMemberApp = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Featured = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ShowBanner = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Redemption = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    PromoCode = table.Column<string>(type: "NVARCHAR(30)", maxLength: 30, nullable: true),
                    PromoCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDraft = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_offers_promo_codes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "promo_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_offers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offers_PromoCodeId",
                table: "offers",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_offers_TenantId_CreatedAtUtc",
                table: "offers",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_offers_TenantId_ShowOnMemberApp_StartDate_EndDate",
                table: "offers",
                columns: new[] { "TenantId", "ShowOnMemberApp", "StartDate", "EndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offers");
        }
    }
}
