using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalSalesContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_sales_contract_documents",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintCount = table.Column<int>(type: "int", nullable: false),
                    LastPrintedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_sales_contract_documents", x => x.Id);
                    table.CheckConstraint("CK_local_sales_contract_documents_language", "[Language] IN ('en','ar')");
                    table.CheckConstraint("CK_local_sales_contract_documents_status", "[Status] IN ('issued')");
                    table.ForeignKey(
                        name: "FK_local_sales_contract_documents_contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "platform",
                        principalTable: "contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_sales_contract_terms",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsEn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TermsAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_sales_contract_terms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_sales_contract_documents_ContractId",
                schema: "platform",
                table: "local_sales_contract_documents",
                column: "ContractId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_sales_contract_documents_ContractNumber",
                schema: "platform",
                table: "local_sales_contract_documents",
                column: "ContractNumber");

            migrationBuilder.CreateIndex(
                name: "IX_local_sales_contract_documents_CustomerId",
                schema: "platform",
                table: "local_sales_contract_documents",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_sales_contract_documents",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "local_sales_contract_terms",
                schema: "platform");
        }
    }
}
