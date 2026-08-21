using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformBillingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_invoice_sequences",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_invoice_sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "platform_invoices",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EtaUuid = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PdfUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_invoices", x => x.Id);
                    table.CheckConstraint("CK_platform_invoices_status", "[Status] IN ('issued','paid','overdue','voided')");
                    table.ForeignKey(
                        name: "FK_platform_invoices_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "platform",
                        principalTable: "subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_platform_invoices_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_platform_invoice_sequences_Year",
                schema: "platform",
                table: "platform_invoice_sequences",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_invoices_DueDate",
                schema: "platform",
                table: "platform_invoices",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_platform_invoices_InvoiceNumber",
                schema: "platform",
                table: "platform_invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_invoices_SubscriptionId_PeriodStart",
                schema: "platform",
                table: "platform_invoices",
                columns: new[] { "SubscriptionId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_invoices_TenantId_Status",
                schema: "platform",
                table: "platform_invoices",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_invoice_sequences",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "platform_invoices",
                schema: "platform");
        }
    }
}
