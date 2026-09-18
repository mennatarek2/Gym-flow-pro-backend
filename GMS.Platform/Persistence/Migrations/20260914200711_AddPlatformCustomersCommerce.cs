using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformCustomersCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('platform.local_licenses', 'CustomerId') IS NULL
    ALTER TABLE [platform].[local_licenses] ADD [CustomerId] uniqueidentifier NULL;
IF COL_LENGTH('platform.local_licenses', 'ContractId') IS NULL
    ALTER TABLE [platform].[local_licenses] ADD [ContractId] uniqueidentifier NULL;
");

            migrationBuilder.CreateTable(
                name: "catalog_products",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProductType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_products", x => x.Id);
                    table.CheckConstraint("CK_catalog_products_type", "[ProductType] IN ('software','hardware','service','consumable','support')");
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    WhatsApp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreferredContactMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LeadSource = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreatedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedSalesRepPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerUsername = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OwnerEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OwnerAccountStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OwnerAccountCreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OwnerLastLoginAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetInitiatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetInitiatedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                    table.CheckConstraint("CK_customers_contact_method", "[PreferredContactMethod] IN ('phone','whatsapp','email')");
                    table.CheckConstraint("CK_customers_status", "[Status] IN ('prospect','active','inactive','churned')");
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_number_sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ContractDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contracts", x => x.Id);
                    table.CheckConstraint("CK_contracts_payment_status", "[PaymentStatus] IN ('unpaid','partial','paid')");
                    table.CheckConstraint("CK_contracts_status", "[Status] IN ('draft','pending','active','completed','expired','cancelled')");
                    table.ForeignKey(
                        name: "FK_contracts_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "platform",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocalLicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocalInstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedToPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_tickets", x => x.Id);
                    table.CheckConstraint("CK_support_tickets_priority", "[Priority] IN ('low','normal','high','critical')");
                    table.CheckConstraint("CK_support_tickets_status", "[Status] IN ('open','in_progress','waiting_customer','resolved','closed')");
                    table.ForeignKey(
                        name: "FK_support_tickets_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "platform",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_items",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SkuSnapshot = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductTypeSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DescriptionSnapshot = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contract_items_catalog_products_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalSchema: "platform",
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_contract_items_contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "platform",
                        principalTable: "contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_payments",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_payments", x => x.Id);
                    table.CheckConstraint("CK_customer_payments_method", "[PaymentMethod] IN ('cash','bank_transfer','other')");
                    table.ForeignKey(
                        name: "FK_customer_payments_contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "platform",
                        principalTable: "contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_payments_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "platform",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_local_licenses_ContractId' AND object_id = OBJECT_ID(N'platform.local_licenses'))
    CREATE INDEX [IX_local_licenses_ContractId] ON [platform].[local_licenses] ([ContractId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_local_licenses_CustomerId' AND object_id = OBJECT_ID(N'platform.local_licenses'))
    CREATE INDEX [IX_local_licenses_CustomerId] ON [platform].[local_licenses] ([CustomerId]);
");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_IsActive",
                schema: "platform",
                table: "catalog_products",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_Sku",
                schema: "platform",
                table: "catalog_products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_items_CatalogProductId",
                schema: "platform",
                table: "contract_items",
                column: "CatalogProductId");

            migrationBuilder.CreateIndex(
                name: "IX_contract_items_ContractId",
                schema: "platform",
                table: "contract_items",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_contracts_ContractNumber",
                schema: "platform",
                table: "contracts",
                column: "ContractNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contracts_CustomerId",
                schema: "platform",
                table: "contracts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_contracts_Status",
                schema: "platform",
                table: "contracts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_customer_payments_ContractId",
                schema: "platform",
                table: "customer_payments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_payments_CustomerId",
                schema: "platform",
                table: "customer_payments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_payments_PaymentDate",
                schema: "platform",
                table: "customer_payments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_customers_AssignedSalesRepPlatformAdminUserId",
                schema: "platform",
                table: "customers",
                column: "AssignedSalesRepPlatformAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_customers_BusinessName",
                schema: "platform",
                table: "customers",
                column: "BusinessName");

            migrationBuilder.CreateIndex(
                name: "IX_customers_Status",
                schema: "platform",
                table: "customers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_customers_TenantId",
                schema: "platform",
                table: "customers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_number_sequences_Kind_Year",
                schema: "platform",
                table: "number_sequences",
                columns: new[] { "Kind", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_AssignedToPlatformAdminUserId",
                schema: "platform",
                table: "support_tickets",
                column: "AssignedToPlatformAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId",
                schema: "platform",
                table: "support_tickets",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_Status",
                schema: "platform",
                table: "support_tickets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_TicketNumber",
                schema: "platform",
                table: "support_tickets",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_local_licenses_contracts_ContractId')
    ALTER TABLE [platform].[local_licenses] WITH CHECK ADD CONSTRAINT [FK_local_licenses_contracts_ContractId]
        FOREIGN KEY ([ContractId]) REFERENCES [platform].[contracts] ([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_local_licenses_customers_CustomerId')
    ALTER TABLE [platform].[local_licenses] WITH CHECK ADD CONSTRAINT [FK_local_licenses_customers_CustomerId]
        FOREIGN KEY ([CustomerId]) REFERENCES [platform].[customers] ([Id]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_local_licenses_contracts_ContractId",
                schema: "platform",
                table: "local_licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_local_licenses_customers_CustomerId",
                schema: "platform",
                table: "local_licenses");

            migrationBuilder.DropTable(
                name: "contract_items",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "customer_payments",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "support_tickets",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "catalog_products",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "contracts",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "platform");

            migrationBuilder.DropIndex(
                name: "IX_local_licenses_ContractId",
                schema: "platform",
                table: "local_licenses");

            migrationBuilder.DropIndex(
                name: "IX_local_licenses_CustomerId",
                schema: "platform",
                table: "local_licenses");

            migrationBuilder.DropColumn(
                name: "ContractId",
                schema: "platform",
                table: "local_licenses");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "platform",
                table: "local_licenses");
        }
    }
}
