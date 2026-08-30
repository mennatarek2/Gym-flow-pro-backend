using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGymMemberIsGuest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "gym_members",
                type: "BIT",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_IsGuest_PhoneNumber",
                table: "gym_members",
                columns: new[] { "TenantId", "IsGuest", "PhoneNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gym_members_TenantId_IsGuest_PhoneNumber",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "gym_members");
        }
    }
}
