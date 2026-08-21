using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "VARCHAR(12)", maxLength: 12, nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "VARCHAR(12)", maxLength: 12, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "NVARCHAR(20)", maxLength: 20, nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MemberNameSnapshot = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: false),
                    MemberPhoneSnapshot = table.Column<string>(type: "NVARCHAR(20)", maxLength: 20, nullable: false),
                    LinesSnapshot = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    VatRate = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Total = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "CHAR(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    IssuedAt = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    PdfUrl = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "VARCHAR(12)", maxLength: 12, nullable: false, defaultValue: "issued"),
                    VoidReason = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.Id);
                    table.CheckConstraint("CK_invoices_LinesSnapshot_IsJson", "ISJSON([LinesSnapshot]) = 1");
                    table.ForeignKey(
                        name: "FK_invoices_app_users_VoidedByUserId",
                        column: x => x.VoidedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_invoices_invoices_OriginalInvoiceId",
                        column: x => x.OriginalInvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_TenantId_Year_Type",
                table: "invoice_sequences",
                columns: new[] { "TenantId", "Year", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_OriginalInvoiceId",
                table: "invoices",
                column: "OriginalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_SaleId",
                table: "invoices",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_InvoiceNumber",
                table: "invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_SaleId",
                table: "invoices",
                columns: new[] { "TenantId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_Status",
                table: "invoices",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_VoidedByUserId",
                table: "invoices",
                column: "VoidedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_sequences");

            migrationBuilder.DropTable(
                name: "invoices");
        }
    }
}
