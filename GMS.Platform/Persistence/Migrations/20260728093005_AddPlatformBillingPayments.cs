using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformBillingPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRenewOptIn",
                schema: "platform",
                table: "subscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SavedCardToken",
                schema: "platform",
                table: "subscriptions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentLink",
                schema: "platform",
                table: "platform_invoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                schema: "platform",
                table: "platform_invoices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_payment_events",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Gateway = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExternalRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    HmacVerified = table.Column<bool>(type: "bit", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_payment_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_platform_payment_events_IdempotencyKey",
                schema: "platform",
                table: "platform_payment_events",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_payment_events_InvoiceId_CreatedAtUtc",
                schema: "platform",
                table: "platform_payment_events",
                columns: new[] { "InvoiceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_payment_events_SubscriptionId_CreatedAtUtc",
                schema: "platform",
                table: "platform_payment_events",
                columns: new[] { "SubscriptionId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_payment_events",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "AutoRenewOptIn",
                schema: "platform",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "SavedCardToken",
                schema: "platform",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PaymentLink",
                schema: "platform",
                table: "platform_invoices");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                schema: "platform",
                table: "platform_invoices");
        }
    }
}
