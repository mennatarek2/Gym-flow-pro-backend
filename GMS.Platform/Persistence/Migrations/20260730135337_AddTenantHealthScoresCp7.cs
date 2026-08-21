using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantHealthScoresCp7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tenant_health_scores_risk_band",
                schema: "platform",
                table: "tenant_health_scores");

            // CP6 seam used green|amber|red — remap before installing CP7 bands.
            migrationBuilder.Sql("""
                UPDATE platform.tenant_health_scores
                SET RiskBand = CASE RiskBand
                    WHEN 'green' THEN 'healthy'
                    WHEN 'amber' THEN 'watch'
                    WHEN 'red' THEN 'at_risk'
                    ELSE RiskBand
                END
                """);

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                schema: "platform",
                table: "tenant_health_scores",
                newName: "computed_at");

            migrationBuilder.RenameColumn(
                name: "BreakdownJson",
                schema: "platform",
                table: "tenant_health_scores",
                newName: "contributing_factors");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAtUtc",
                schema: "platform",
                table: "tenant_health_scores",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedPlatformUserId",
                schema: "platform",
                table: "tenant_health_scores",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "risk_queue_outcomes",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_queue_outcomes", x => x.Id);
                    table.CheckConstraint("CK_risk_queue_outcomes_outcome", "[Outcome] IN ('contacted','retained','churned','no_answer','watching')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_health_scores_RiskBand",
                schema: "platform",
                table: "tenant_health_scores",
                column: "RiskBand");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_health_scores_RiskBand_Score",
                schema: "platform",
                table: "tenant_health_scores",
                columns: new[] { "RiskBand", "Score" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenant_health_scores_risk_band",
                schema: "platform",
                table: "tenant_health_scores",
                sql: "[RiskBand] IN ('healthy','watch','at_risk','critical')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenant_health_scores_score",
                schema: "platform",
                table: "tenant_health_scores",
                sql: "[Score] >= 0 AND [Score] <= 100");

            migrationBuilder.CreateIndex(
                name: "IX_risk_queue_outcomes_TenantId_CreatedAtUtc",
                schema: "platform",
                table: "risk_queue_outcomes",
                columns: new[] { "TenantId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_queue_outcomes",
                schema: "platform");

            migrationBuilder.DropIndex(
                name: "IX_tenant_health_scores_RiskBand",
                schema: "platform",
                table: "tenant_health_scores");

            migrationBuilder.DropIndex(
                name: "IX_tenant_health_scores_RiskBand_Score",
                schema: "platform",
                table: "tenant_health_scores");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tenant_health_scores_risk_band",
                schema: "platform",
                table: "tenant_health_scores");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tenant_health_scores_score",
                schema: "platform",
                table: "tenant_health_scores");

            migrationBuilder.DropColumn(
                name: "AssignedAtUtc",
                schema: "platform",
                table: "tenant_health_scores");

            migrationBuilder.DropColumn(
                name: "AssignedPlatformUserId",
                schema: "platform",
                table: "tenant_health_scores");

            migrationBuilder.RenameColumn(
                name: "contributing_factors",
                schema: "platform",
                table: "tenant_health_scores",
                newName: "BreakdownJson");

            migrationBuilder.RenameColumn(
                name: "computed_at",
                schema: "platform",
                table: "tenant_health_scores",
                newName: "UpdatedAtUtc");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenant_health_scores_risk_band",
                schema: "platform",
                table: "tenant_health_scores",
                sql: "[RiskBand] IN ('green','amber','red')");
        }
    }
}
