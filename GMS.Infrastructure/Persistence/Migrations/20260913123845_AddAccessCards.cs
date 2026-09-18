using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false, defaultValue: "Available"),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    LostAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    Reason = table.Column<string>(type: "NVARCHAR(240)", maxLength: 240, nullable: true),
                    BatchId = table.Column<string>(type: "VARCHAR(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_cards", x => x.Id);
                    table.CheckConstraint("CK_access_cards_Status", "Status IN ('Available','Assigned','Lost','Damaged','Blocked')");
                    table.ForeignKey(
                        name: "FK_access_cards_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_cards_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_cards_MemberId",
                table: "access_cards",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_access_cards_TenantId_BatchId",
                table: "access_cards",
                columns: new[] { "TenantId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_access_cards_TenantId_Code",
                table: "access_cards",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_access_cards_TenantId_MemberId",
                table: "access_cards",
                columns: new[] { "TenantId", "MemberId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 'Assigned' AND [MemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_access_cards_TenantId_Status",
                table: "access_cards",
                columns: new[] { "TenantId", "Status" });

            // Legacy backfill: each non-deleted member gets Assigned card with Code = MemberNumber.
            // Preserves printed CODE128 cards; renew/expire do not free these rows.
            migrationBuilder.Sql(@"
INSERT INTO access_cards (TenantId, Code, Status, MemberId, AssignedAtUtc, CreatedAtUtc, IsDeleted)
SELECT m.TenantId, m.MemberNumber, 'Assigned', m.Id, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
FROM gym_members m
WHERE m.IsDeleted = 0
  AND m.MemberNumber IS NOT NULL
  AND LTRIM(RTRIM(m.MemberNumber)) <> ''
  AND NOT EXISTS (
      SELECT 1 FROM access_cards c
      WHERE c.TenantId = m.TenantId
        AND c.Code = m.MemberNumber
        AND c.IsDeleted = 0);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_cards");
        }
    }
}
