using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GMS.Platform.Persistence;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    [DbContext(typeof(PlatformDbContext))]
    [Migration("20260916053000_AddCustomerTenantFk")]
    public partial class AddCustomerTenantFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE platform.customers
                SET TenantId = NULL
                WHERE TenantId IS NOT NULL
                  AND TenantId NOT IN (SELECT Id FROM dbo.tenants);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_customers_tenants_TenantId",
                schema: "platform",
                table: "customers",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customers_tenants_TenantId",
                schema: "platform",
                table: "customers");
        }
    }
}
