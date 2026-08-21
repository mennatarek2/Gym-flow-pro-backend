using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialAndDayPassSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrialVisitLimit",
                table: "membership_plans",
                type: "INT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConvertingSaleId",
                table: "gym_members",
                type: "UNIQUEIDENTIFIER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrial",
                table: "gym_members",
                type: "BIT",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialConvertedAt",
                table: "gym_members",
                type: "DATETIME2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrialOutcome",
                table: "gym_members",
                type: "VARCHAR(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_membership_plans_PlanType",
                table: "membership_plans",
                sql: "PlanType IN ('monthly_unlimited','session_pack','time_limited','pt_credits','family','trial','day_pass')");

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_ConvertingSaleId",
                table: "gym_members",
                column: "ConvertingSaleId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_gym_members_TrialOutcome",
                table: "gym_members",
                sql: "TrialOutcome IS NULL OR TrialOutcome IN ('active_trial','converted','expired')");

            migrationBuilder.AddForeignKey(
                name: "FK_gym_members_sales_ConvertingSaleId",
                table: "gym_members",
                column: "ConvertingSaleId",
                principalTable: "sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gym_members_sales_ConvertingSaleId",
                table: "gym_members");

            migrationBuilder.DropCheckConstraint(
                name: "CK_membership_plans_PlanType",
                table: "membership_plans");

            migrationBuilder.DropIndex(
                name: "IX_gym_members_ConvertingSaleId",
                table: "gym_members");

            migrationBuilder.DropCheckConstraint(
                name: "CK_gym_members_TrialOutcome",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "TrialVisitLimit",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "ConvertingSaleId",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "IsTrial",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "TrialConvertedAt",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "TrialOutcome",
                table: "gym_members");
        }
    }
}
