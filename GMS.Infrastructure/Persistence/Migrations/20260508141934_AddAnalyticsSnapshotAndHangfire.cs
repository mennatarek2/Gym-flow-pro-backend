using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsSnapshotAndHangfire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gym_analytics_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    ActiveMembers = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ExpiredMembers = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    FrozenMembers = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NewMembersThisMonth = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    RevenueThisMonth = table.Column<decimal>(type: "DECIMAL(14,2)", nullable: false, defaultValue: 0m),
                    Currency = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false, defaultValue: "EGP"),
                    CheckinsToday = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CheckinsThisMonth = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    InvitationsSentThisMonth = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    InvitationsConvertedThisMonth = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gym_analytics_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gym_analytics_snapshots_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gym_analytics_snapshots_TenantId_SnapshotDate",
                table: "gym_analytics_snapshots",
                columns: new[] { "TenantId", "SnapshotDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gym_analytics_snapshots");
        }
    }
}
