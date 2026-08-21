using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationReferralSchemaInv1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralRewardType",
                table: "membership_plans",
                type: "VARCHAR(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferralRewardValue",
                table: "membership_plans",
                type: "DECIMAL(12,2)",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "VisitDate",
                table: "member_invitations",
                type: "DATE",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "DATE");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "member_invitations",
                type: "VARCHAR(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)",
                oldMaxLength: 15,
                oldDefaultValue: "pending");

            migrationBuilder.AddColumn<Guid>(
                name: "ConvertingSaleId",
                table: "member_invitations",
                type: "UNIQUEIDENTIFIER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvitationType",
                table: "member_invitations",
                type: "VARCHAR(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "guest_pass");

            migrationBuilder.AddColumn<string>(
                name: "ReferralCodeUsed",
                table: "member_invitations",
                type: "NVARCHAR(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralCode",
                table: "gym_members",
                type: "NVARCHAR(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralTier",
                table: "gym_members",
                type: "VARCHAR(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuccessfulReferralCount",
                table: "gym_members",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing invites are guest passes (column default also applied).
            migrationBuilder.Sql(
                "UPDATE member_invitations SET InvitationType = 'guest_pass' WHERE InvitationType IS NULL OR InvitationType = '';");

            // Unique share codes for existing members (filtered unique index after).
            migrationBuilder.Sql(@"
UPDATE gm
SET ReferralCode = 'R' + UPPER(SUBSTRING(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 1, 7))
FROM gym_members AS gm
WHERE gm.ReferralCode IS NULL AND gm.IsDeleted = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId_InvitationType",
                table: "member_invitations",
                columns: new[] { "TenantId", "InvitationType" });

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_ReferralCode",
                table: "gym_members",
                columns: new[] { "TenantId", "ReferralCode" },
                unique: true,
                filter: "[ReferralCode] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_member_invitations_TenantId_InvitationType",
                table: "member_invitations");

            migrationBuilder.DropIndex(
                name: "IX_gym_members_TenantId_ReferralCode",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "ReferralRewardType",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "ReferralRewardValue",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "ConvertingSaleId",
                table: "member_invitations");

            migrationBuilder.DropColumn(
                name: "InvitationType",
                table: "member_invitations");

            migrationBuilder.DropColumn(
                name: "ReferralCodeUsed",
                table: "member_invitations");

            migrationBuilder.DropColumn(
                name: "ReferralCode",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "ReferralTier",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "SuccessfulReferralCount",
                table: "gym_members");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "VisitDate",
                table: "member_invitations",
                type: "DATE",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "DATE",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "member_invitations",
                type: "VARCHAR(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "VARCHAR(20)",
                oldMaxLength: 20,
                oldDefaultValue: "pending");
        }
    }
}
