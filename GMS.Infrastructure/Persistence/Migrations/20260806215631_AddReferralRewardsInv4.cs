using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralRewardsInv4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_member_credits_EntryType",
                table: "member_credits");

            migrationBuilder.CreateTable(
                name: "referral_rewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BeneficiaryMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeneficiaryRole = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    RewardType = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    RewardValue = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(15)", maxLength: 15, nullable: false),
                    HoldUntilUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    ForfeitedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CreditEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExtendedMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DaysGranted = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referral_rewards", x => x.Id);
                    table.CheckConstraint("CK_referral_rewards_BeneficiaryRole", "BeneficiaryRole IN ('referrer','referee')");
                    table.CheckConstraint("CK_referral_rewards_RewardType", "RewardType IN ('credit','free_days')");
                    table.CheckConstraint("CK_referral_rewards_Status", "Status IN ('pending_hold','granted','reversed','forfeited')");
                    table.ForeignKey(
                        name: "FK_referral_rewards_gym_members_BeneficiaryMemberId",
                        column: x => x.BeneficiaryMemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_referral_rewards_member_invitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "member_invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_referral_rewards_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_member_credits_EntryType",
                table: "member_credits",
                sql: "EntryType IN ('refund','payment_use','adjustment','referral_reward')");

            migrationBuilder.CreateIndex(
                name: "IX_referral_rewards_BeneficiaryMemberId",
                table: "referral_rewards",
                column: "BeneficiaryMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_referral_rewards_InvitationId",
                table: "referral_rewards",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_referral_rewards_TenantId_InvitationId_BeneficiaryRole",
                table: "referral_rewards",
                columns: new[] { "TenantId", "InvitationId", "BeneficiaryRole" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_referral_rewards_TenantId_SaleId",
                table: "referral_rewards",
                columns: new[] { "TenantId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_referral_rewards_TenantId_Status_HoldUntilUtc",
                table: "referral_rewards",
                columns: new[] { "TenantId", "Status", "HoldUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "referral_rewards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_member_credits_EntryType",
                table: "member_credits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_member_credits_EntryType",
                table: "member_credits",
                sql: "EntryType IN ('refund','payment_use','adjustment')");
        }
    }
}
