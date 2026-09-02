using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialRemediationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_transactions_ExternalRef' AND object_id = OBJECT_ID('payment_transactions'))
                    DROP INDEX [IX_payment_transactions_ExternalRef] ON [payment_transactions];

                IF COL_LENGTH('sale_lines', 'CogsAmount') IS NULL
                    ALTER TABLE [sale_lines] ADD [CogsAmount] DECIMAL(14,2) NULL;

                IF COL_LENGTH('sale_lines', 'UnitCost') IS NULL
                    ALTER TABLE [sale_lines] ADD [UnitCost] DECIMAL(12,2) NULL;

                IF COL_LENGTH('payment_transactions', 'SettledAtUtc') IS NULL
                    ALTER TABLE [payment_transactions] ADD [SettledAtUtc] DATETIME2 NULL;

                IF COL_LENGTH('payment_transactions', 'SettlementStatus') IS NULL
                    ALTER TABLE [payment_transactions] ADD [SettlementStatus] VARCHAR(20) NOT NULL
                        CONSTRAINT [DF_payment_transactions_SettlementStatus] DEFAULT ('unknown');

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_transactions_TenantId_Gateway_ExternalRef' AND object_id = OBJECT_ID('payment_transactions'))
                    CREATE UNIQUE INDEX [IX_payment_transactions_TenantId_Gateway_ExternalRef]
                        ON [payment_transactions] ([TenantId], [Gateway], [ExternalRef]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_transactions_TenantId_Gateway_ExternalRef' AND object_id = OBJECT_ID('payment_transactions'))
                    DROP INDEX [IX_payment_transactions_TenantId_Gateway_ExternalRef] ON [payment_transactions];
                IF COL_LENGTH('sale_lines', 'CogsAmount') IS NOT NULL
                    ALTER TABLE [sale_lines] DROP COLUMN [CogsAmount];
                IF COL_LENGTH('sale_lines', 'UnitCost') IS NOT NULL
                    ALTER TABLE [sale_lines] DROP COLUMN [UnitCost];
                IF COL_LENGTH('payment_transactions', 'SettledAtUtc') IS NOT NULL
                    ALTER TABLE [payment_transactions] DROP COLUMN [SettledAtUtc];
                IF COL_LENGTH('payment_transactions', 'SettlementStatus') IS NOT NULL
                    ALTER TABLE [payment_transactions] DROP COLUMN [SettlementStatus];
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_transactions_ExternalRef' AND object_id = OBJECT_ID('payment_transactions'))
                    CREATE UNIQUE INDEX [IX_payment_transactions_ExternalRef]
                        ON [payment_transactions] ([ExternalRef]);
                """);
        }
    }
}
