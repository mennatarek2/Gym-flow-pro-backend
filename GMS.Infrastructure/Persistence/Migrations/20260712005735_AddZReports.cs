using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddZReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "z_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    PayloadJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    PdfUrl = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_z_reports", x => x.Id);
                    table.CheckConstraint("CK_z_reports_PayloadJson_IsJson", "ISJSON([PayloadJson]) = 1");
                    table.ForeignKey(
                        name: "FK_z_reports_app_users_GeneratedByUserId",
                        column: x => x.GeneratedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_z_reports_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_z_reports_GeneratedByUserId",
                table: "z_reports",
                column: "GeneratedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_z_reports_TenantId_ReportDate",
                table: "z_reports",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "z_reports");
        }
    }
}
