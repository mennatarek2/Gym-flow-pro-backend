using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_call_outcomes_Outcome",
                table: "call_outcomes");

            migrationBuilder.AlterColumn<string>(
                name: "Outcome",
                table: "call_outcomes",
                type: "VARCHAR(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)",
                oldMaxLength: 15);

            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "call_outcomes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "FollowUpId",
                table: "call_outcomes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MemberId",
                table: "call_outcomes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextAction",
                table: "call_outcomes",
                type: "VARCHAR(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextActionAtUtc",
                table: "call_outcomes",
                type: "DATETIME2",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE c SET c.MemberId = m.MemberId
FROM call_outcomes c
INNER JOIN memberships m ON m.Id = c.MembershipId
WHERE c.MemberId IS NULL AND c.MembershipId IS NOT NULL;
");

            migrationBuilder.CreateTable(
                name: "member_follow_ups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    SourceKey = table.Column<string>(type: "VARCHAR(80)", maxLength: 80, nullable: false),
                    Priority = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    NextAction = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: true),
                    NextActionAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    RelatedType = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: true),
                    RelatedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Why = table.Column<string>(type: "NVARCHAR(240)", maxLength: 240, nullable: true),
                    Notes = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_follow_ups", x => x.Id);
                    table.CheckConstraint("CK_member_follow_ups_Priority", "Priority IN ('high','medium','low')");
                    table.CheckConstraint("CK_member_follow_ups_Reason", "Reason IN ('renewal','trial','payment','welcome','inactive','offer','custom')");
                    table.CheckConstraint("CK_member_follow_ups_Source", "Source IN ('system','manual')");
                    table.CheckConstraint("CK_member_follow_ups_Status", "Status IN ('pending','in_progress','contacted','no_answer','completed','cancelled')");
                    table.ForeignKey(
                        name: "FK_member_follow_ups_app_users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_follow_ups_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_follow_ups_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_follow_ups_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_call_outcomes_FollowUpId",
                table: "call_outcomes",
                column: "FollowUpId");

            migrationBuilder.CreateIndex(
                name: "IX_call_outcomes_MemberId",
                table: "call_outcomes",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_call_outcomes_TenantId_FollowUpId",
                table: "call_outcomes",
                columns: new[] { "TenantId", "FollowUpId" });

            migrationBuilder.CreateIndex(
                name: "IX_call_outcomes_TenantId_MemberId",
                table: "call_outcomes",
                columns: new[] { "TenantId", "MemberId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_call_outcomes_Outcome",
                table: "call_outcomes",
                sql: "Outcome IN ('contacted','renewed','declined','no_answer','reached','busy','wrong_number','not_interested','will_visit','needs_follow_up')");

            migrationBuilder.CreateIndex(
                name: "IX_member_follow_ups_AssignedToUserId",
                table: "member_follow_ups",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_follow_ups_MemberId",
                table: "member_follow_ups",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_member_follow_ups_MembershipId",
                table: "member_follow_ups",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_member_follow_ups_TenantId_DueAtUtc_Status",
                table: "member_follow_ups",
                columns: new[] { "TenantId", "DueAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_member_follow_ups_TenantId_MemberId",
                table: "member_follow_ups",
                columns: new[] { "TenantId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_member_follow_ups_TenantId_SourceKey",
                table: "member_follow_ups",
                columns: new[] { "TenantId", "SourceKey" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] IN ('pending','in_progress','contacted','no_answer')");

            migrationBuilder.AddForeignKey(
                name: "FK_call_outcomes_gym_members_MemberId",
                table: "call_outcomes",
                column: "MemberId",
                principalTable: "gym_members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_call_outcomes_member_follow_ups_FollowUpId",
                table: "call_outcomes",
                column: "FollowUpId",
                principalTable: "member_follow_ups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_call_outcomes_gym_members_MemberId",
                table: "call_outcomes");

            migrationBuilder.DropForeignKey(
                name: "FK_call_outcomes_member_follow_ups_FollowUpId",
                table: "call_outcomes");

            migrationBuilder.DropTable(
                name: "member_follow_ups");

            migrationBuilder.DropIndex(
                name: "IX_call_outcomes_FollowUpId",
                table: "call_outcomes");

            migrationBuilder.DropIndex(
                name: "IX_call_outcomes_MemberId",
                table: "call_outcomes");

            migrationBuilder.DropIndex(
                name: "IX_call_outcomes_TenantId_FollowUpId",
                table: "call_outcomes");

            migrationBuilder.DropIndex(
                name: "IX_call_outcomes_TenantId_MemberId",
                table: "call_outcomes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_call_outcomes_Outcome",
                table: "call_outcomes");

            migrationBuilder.DropColumn(
                name: "FollowUpId",
                table: "call_outcomes");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "call_outcomes");

            migrationBuilder.DropColumn(
                name: "NextAction",
                table: "call_outcomes");

            migrationBuilder.DropColumn(
                name: "NextActionAtUtc",
                table: "call_outcomes");

            migrationBuilder.AlterColumn<string>(
                name: "Outcome",
                table: "call_outcomes",
                type: "VARCHAR(15)",
                maxLength: 15,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "call_outcomes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_call_outcomes_Outcome",
                table: "call_outcomes",
                sql: "Outcome IN ('contacted','renewed','declined','no_answer')");
        }
    }
}
