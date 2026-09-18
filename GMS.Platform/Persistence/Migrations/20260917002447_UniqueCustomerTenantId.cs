using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueCustomerTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customers_TenantId",
                schema: "platform",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "UX_customers_TenantId",
                schema: "platform",
                table: "customers",
                column: "TenantId",
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_customers_TenantId",
                schema: "platform",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "IX_customers_TenantId",
                schema: "platform",
                table: "customers",
                column: "TenantId");
        }
    }
}
