using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundsAndMemberCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "member_credits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    EntryType = table.Column<string>(type: "VARCHAR(15)", maxLength: 15, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_credits", x => x.Id);
                    table.CheckConstraint("CK_member_credits_EntryType", "EntryType IN ('refund','payment_use','adjustment')");
                    table.ForeignKey(
                        name: "FK_member_credits_app_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_credits_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_credits_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Method = table.Column<string>(type: "VARCHAR(15)", maxLength: 15, nullable: false),
                    Reason = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "VARCHAR(12)", maxLength: 12, nullable: false, defaultValue: "requested"),
                    RejectionNote = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: true),
                    CreditNoteInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.Id);
                    table.CheckConstraint("CK_refunds_Method", "Method IN ('cash','gateway','credit')");
                    table.CheckConstraint("CK_refunds_Status", "Status IN ('requested','approved','executed','rejected')");
                    table.ForeignKey(
                        name: "FK_refunds_app_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_refunds_app_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refunds_invoices_CreditNoteInvoiceId",
                        column: x => x.CreditNoteInvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_refunds_payment_transactions_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "payment_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_refunds_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refunds_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_RefundId",
                table: "invoices",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_member_credits_CreatedByUserId",
                table: "member_credits",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_credits_MemberId",
                table: "member_credits",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_member_credits_TenantId_MemberId",
                table: "member_credits",
                columns: new[] { "TenantId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_refunds_ApprovedByUserId",
                table: "refunds",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_CreditNoteInvoiceId",
                table: "refunds",
                column: "CreditNoteInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_PaymentTransactionId",
                table: "refunds",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_RequestedByUserId",
                table: "refunds",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_SaleId",
                table: "refunds",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_TenantId_SaleId",
                table: "refunds",
                columns: new[] { "TenantId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_refunds_TenantId_Status",
                table: "refunds",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_refunds_RefundId",
                table: "invoices",
                column: "RefundId",
                principalTable: "refunds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoices_refunds_RefundId",
                table: "invoices");

            migrationBuilder.DropTable(
                name: "member_credits");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropIndex(
                name: "IX_invoices_RefundId",
                table: "invoices");
        }
    }
}
