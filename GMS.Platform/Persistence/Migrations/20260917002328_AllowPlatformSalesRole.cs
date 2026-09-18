using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowPlatformSalesRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_platform_admin_users_role",
                schema: "platform",
                table: "platform_admin_users");

            migrationBuilder.AddCheckConstraint(
                name: "CK_platform_admin_users_role",
                schema: "platform",
                table: "platform_admin_users",
                sql: "[Role] IN ('platform_support','platform_ops','platform_admin','platform_sales')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_platform_admin_users_role",
                schema: "platform",
                table: "platform_admin_users");

            migrationBuilder.AddCheckConstraint(
                name: "CK_platform_admin_users_role",
                schema: "platform",
                table: "platform_admin_users",
                sql: "[Role] IN ('platform_support','platform_ops','platform_admin')");
        }
    }
}
