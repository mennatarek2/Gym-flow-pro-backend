using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyInvitationsProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReferralInviteQuota",
                table: "membership_plans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "InvitationType",
                table: "member_invitations",
                type: "VARCHAR(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "invitation",
                oldClrType: typeof(string),
                oldType: "VARCHAR(12)",
                oldMaxLength: 12,
                oldDefaultValue: "guest_pass");

            migrationBuilder.AddColumn<DateTime>(
                name: "ContactedAtUtc",
                table: "member_invitations",
                type: "DATETIME2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CoveringMembershipId",
                table: "member_invitations",
                type: "UNIQUEIDENTIFIER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalIdEncrypted",
                table: "member_invitations",
                type: "NVARCHAR(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "member_invitations",
                type: "NVARCHAR(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId_CoveringMembershipId",
                table: "member_invitations",
                columns: new[] { "TenantId", "CoveringMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId_GuestPhoneNumber_InvitationType",
                table: "member_invitations",
                columns: new[] { "TenantId", "GuestPhoneNumber", "InvitationType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_member_invitations_TenantId_CoveringMembershipId",
                table: "member_invitations");

            migrationBuilder.DropIndex(
                name: "IX_member_invitations_TenantId_GuestPhoneNumber_InvitationType",
                table: "member_invitations");

            migrationBuilder.DropColumn(
                name: "ReferralInviteQuota",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "ContactedAtUtc",
                table: "member_invitations");

            migrationBuilder.DropColumn(
                name: "CoveringMembershipId",
                table: "member_invitations");

            migrationBuilder.DropColumn(
                name: "NationalIdEncrypted",
                table: "member_invitations");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "member_invitations");

            migrationBuilder.AlterColumn<string>(
                name: "InvitationType",
                table: "member_invitations",
                type: "VARCHAR(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "guest_pass",
                oldClrType: typeof(string),
                oldType: "VARCHAR(20)",
                oldMaxLength: 20,
                oldDefaultValue: "invitation");
        }
    }
}
