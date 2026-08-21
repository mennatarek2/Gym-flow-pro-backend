using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: false),
                    FileBlobUrl = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: false),
                    EntityScope = table.Column<string>(type: "VARCHAR(20)", maxLength: 20, nullable: false, defaultValue: "members_memberships"),
                    Status = table.Column<string>(type: "VARCHAR(12)", maxLength: 12, nullable: false, defaultValue: "validating"),
                    TotalRows = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OkRows = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ErrorRows = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MappingJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.Id);
                    table.CheckConstraint("CK_import_batches_EntityScope", "EntityScope IN ('members_memberships')");
                    table.CheckConstraint("CK_import_batches_Status", "Status IN ('validating','dry_run_ready','importing','completed','rolled_back','failed')");
                    table.ForeignKey(
                        name: "FK_import_batches_app_users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_import_batches_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_rows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    RawJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(12)", maxLength: 12, nullable: false, defaultValue: "ok"),
                    ErrorCodes = table.Column<string>(type: "NVARCHAR(400)", maxLength: 400, nullable: true),
                    CreatedMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_rows", x => x.Id);
                    table.CheckConstraint("CK_import_rows_Status", "Status IN ('ok','error','imported','skipped')");
                    table.ForeignKey(
                        name: "FK_import_rows_import_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "import_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_import_rows_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_TenantId_Status",
                table: "import_batches",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_UploadedByUserId",
                table: "import_batches",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_import_rows_BatchId_RowNumber",
                table: "import_rows",
                columns: new[] { "BatchId", "RowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_import_rows_TenantId",
                table: "import_rows",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_rows");

            migrationBuilder.DropTable(
                name: "import_batches");
        }
    }
}
