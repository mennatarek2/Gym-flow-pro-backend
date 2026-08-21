using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    // ============================================================================================
    // SCHEMA DIAGRAM — P5 commercial data model (sales/POS foundation)
    // ============================================================================================
    //
    //   tenants
    //     +-- Settings NVARCHAR(MAX) NULL  [CK_tenants_Settings_IsJson: NULL OR ISJSON()=1]
    //         (see GMS.Core.Constants.TenantSettingsKeys for the documented JSON keys)
    //
    //   gym_members
    //     +-- PaperWaiverOnFile BIT NOT NULL DEFAULT 0
    //
    //   gym_attendance
    //     +-- PresenceStatus VARCHAR(12) NULL
    //     +-- DeviceFingerprint NVARCHAR(100) NULL
    //
    //   payment_transactions            (existing MemberId/MembershipId/Gateway/ExternalRef/Status/
    //                                    RawPayload/HmacVerified/PaidAtUtc columns UNCHANGED)
    //     +-- SaleId UNIQUEIDENTIFIER NULL          -> sales.Id   [NO FK YET — deferred to P5* migration]
    //     +-- ReceivedByUserId UNIQUEIDENTIFIER NULL -> app_users.Id  [FK, ON DELETE SET NULL]
    //     +-- ShiftId UNIQUEIDENTIFIER NULL          -> shifts.Id  [NO FK YET — table doesn't exist, deferred to P8]
    //     +-- Method VARCHAR(20) NULL   (cash|card_paymob|fawry|vodafone|instapay|account_credit)
    //
    //   promo_codes                                             (new table)
    //     PK Id, TenantId --Restrict--> tenants
    //     UNIQUE (TenantId, Code)
    //     Code NVARCHAR(30), Type VARCHAR(10) ('percent'|'fixed'), Value DECIMAL(12,2),
    //     AppliesTo NVARCHAR(MAX) NULL (JSON array of plan ids, null = all plans),
    //     ValidFrom/ValidTo DATE, MaxUses/MaxUsesPerMember INT NULL, UsesCount INT DEFAULT 0,
    //     MinPrice DECIMAL(12,2) NULL, IsActive BIT DEFAULT 1
    //
    //   sales                                                   (new table)
    //     PK Id, TenantId --Restrict--> tenants
    //     MemberId NULL --Restrict--> gym_members.Id
    //     SoldByUserId NOT NULL --Restrict--> app_users.Id
    //     ShiftId NULL -> shifts.Id  [NO FK YET — deferred to P8]
    //     PromoCodeId NULL --Restrict--> promo_codes.Id
    //     Subtotal/DiscountAmount/TaxAmount/Total DECIMAL(12,2)
    //     ManualDiscountAmount DECIMAL(12,2) NULL, ManualDiscountReason NVARCHAR(200) NULL
    //     AmountDue DECIMAL(12,2) NOT NULL DEFAULT 0, DueDate DATE NULL
    //     Status VARCHAR(15) ('completed'|'partially_paid'|'refunded'|'partially_refunded')
    //     IdempotencyKey NVARCHAR(100) NULL
    //     INDEX (TenantId, Status), (TenantId, MemberId)
    //     UNIQUE (TenantId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL
    //
    //   sale_lines                                              (new table)
    //     PK Id, TenantId --Restrict--> tenants
    //     SaleId NOT NULL --Cascade--> sales.Id   (single-parent ownership, no competing cascade path)
    //     LineType VARCHAR(15) ('membership'|'trial'|'day_pass'|'retail'|'fee')
    //     ReferenceId UNIQUEIDENTIFIER NULL (polymorphic, no FK — depends on LineType)
    //     Description NVARCHAR(300), DescriptionAr NVARCHAR(300) NULL
    //     Qty INT DEFAULT 1, UnitPrice/LineTotal DECIMAL(12,2)
    //     INDEX (TenantId, SaleId)
    //
    //   sale_idempotency_keys                                   (new table — NOT a BaseEntity, no soft delete)
    //     PK Id
    //     TenantId (no global query filter — always queried by explicit TenantId)
    //     SaleId NOT NULL --Restrict--> sales.Id
    //     Key NVARCHAR(100), ResponseHash NVARCHAR(64), CreatedAt DATETIME2
    //     UNIQUE (TenantId, Key)
    //
    // ============================================================================================

    /// <inheritdoc />
    public partial class AddCommercialDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Settings",
                table: "tenants",
                type: "NVARCHAR(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "payment_transactions",
                type: "VARCHAR(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivedByUserId",
                table: "payment_transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "payment_transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShiftId",
                table: "payment_transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PaperWaiverOnFile",
                table: "gym_members",
                type: "BIT",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeviceFingerprint",
                table: "gym_attendance",
                type: "NVARCHAR(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresenceStatus",
                table: "gym_attendance",
                type: "VARCHAR(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promo_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "NVARCHAR(30)", maxLength: 30, nullable: false),
                    Type = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    AppliesTo = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "DATE", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "DATE", nullable: false),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    MaxUsesPerMember = table.Column<int>(type: "int", nullable: true),
                    UsesCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MinPrice = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promo_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promo_codes_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SoldByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Subtotal = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Total = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    PromoCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ManualDiscountAmount = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: true),
                    ManualDiscountReason = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: true),
                    AmountDue = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false, defaultValue: 0m),
                    DueDate = table.Column<DateOnly>(type: "DATE", nullable: true),
                    Status = table.Column<string>(type: "VARCHAR(15)", maxLength: 15, nullable: false, defaultValue: "completed"),
                    IdempotencyKey = table.Column<string>(type: "NVARCHAR(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_app_users_SoldByUserId",
                        column: x => x.SoldByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_promo_codes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "promo_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_idempotency_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "NVARCHAR(100)", maxLength: 100, nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseHash = table.Column<string>(type: "NVARCHAR(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_idempotency_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sale_idempotency_keys_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineType = table.Column<string>(type: "VARCHAR(15)", maxLength: 15, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: false),
                    DescriptionAr = table.Column<string>(type: "NVARCHAR(300)", maxLength: 300, nullable: true),
                    Qty = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sale_lines_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sale_lines_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenants_Settings_IsJson",
                table: "tenants",
                sql: "[Settings] IS NULL OR ISJSON([Settings]) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_ReceivedByUserId",
                table: "payment_transactions",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_promo_codes_TenantId_Code",
                table: "promo_codes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_idempotency_keys_SaleId",
                table: "sale_idempotency_keys",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_idempotency_keys_TenantId_Key",
                table: "sale_idempotency_keys",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_lines_SaleId",
                table: "sale_lines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_lines_TenantId_SaleId",
                table: "sale_lines",
                columns: new[] { "TenantId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_MemberId",
                table: "sales",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_PromoCodeId",
                table: "sales",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_SoldByUserId",
                table: "sales",
                column: "SoldByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_TenantId_IdempotencyKey",
                table: "sales",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_TenantId_MemberId",
                table: "sales",
                columns: new[] { "TenantId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_TenantId_Status",
                table: "sales",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_transactions_app_users_ReceivedByUserId",
                table: "payment_transactions",
                column: "ReceivedByUserId",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_transactions_app_users_ReceivedByUserId",
                table: "payment_transactions");

            migrationBuilder.DropTable(
                name: "sale_idempotency_keys");

            migrationBuilder.DropTable(
                name: "sale_lines");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropTable(
                name: "promo_codes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tenants_Settings_IsJson",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_ReceivedByUserId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "Settings",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ReceivedByUserId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "PaperWaiverOnFile",
                table: "gym_members");

            migrationBuilder.DropColumn(
                name: "DeviceFingerprint",
                table: "gym_attendance");

            migrationBuilder.DropColumn(
                name: "PresenceStatus",
                table: "gym_attendance");
        }
    }
}
