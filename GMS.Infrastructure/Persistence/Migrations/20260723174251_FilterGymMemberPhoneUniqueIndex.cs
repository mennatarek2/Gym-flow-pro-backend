using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilterGymMemberPhoneUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gym_members_TenantId_PhoneNumber",
                table: "gym_members");

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_PhoneNumber",
                table: "gym_members",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gym_members_TenantId_PhoneNumber",
                table: "gym_members");

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_PhoneNumber",
                table: "gym_members",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true);
        }
    }
}
