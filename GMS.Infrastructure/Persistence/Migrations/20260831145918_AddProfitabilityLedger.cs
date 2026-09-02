using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using GMS.Infrastructure.Persistence;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Compatibility migration for the migration ID already recorded by the
/// existing database. The financial ledger tables were introduced by the
/// preceding cash-expense migration; this preserves the recorded history
/// without inventing another schema change.
/// </summary>
[DbContext(typeof(GymFlowProDbContext))]
[Migration("20260831145918_AddProfitabilityLedger")]
public partial class AddProfitabilityLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // This column is part of the schema represented by the already-applied
        // migration ID. It must exist before the later structured-fields
        // migration creates its filtered unique index.
        migrationBuilder.Sql("""
            IF COL_LENGTH('cash_expenses', 'IdempotencyKey') IS NULL
                ALTER TABLE [cash_expenses] ADD [IdempotencyKey] varchar(100) NULL;
            IF COL_LENGTH('cash_expenses', 'CashMovementId') IS NULL
                ALTER TABLE [cash_expenses] ADD [CashMovementId] uniqueidentifier NULL;
            IF COL_LENGTH('cash_expenses', 'SourceId') IS NULL
                ALTER TABLE [cash_expenses] ADD [SourceId] uniqueidentifier NULL;
            IF COL_LENGTH('cash_expenses', 'SourceType') IS NULL
                ALTER TABLE [cash_expenses] ADD [SourceType] varchar(40) NOT NULL
                    CONSTRAINT [DF_cash_expenses_SourceType] DEFAULT ('manual');
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = 'IX_cash_expenses_TenantId_SourceType_SourceId'
                             AND object_id = OBJECT_ID('cash_expenses'))
                CREATE INDEX [IX_cash_expenses_TenantId_SourceType_SourceId]
                    ON [cash_expenses] ([TenantId], [SourceType], [SourceId]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('cash_expenses', 'IdempotencyKey') IS NOT NULL
                ALTER TABLE [cash_expenses] DROP COLUMN [IdempotencyKey];
            IF EXISTS (SELECT 1 FROM sys.indexes
                       WHERE name = 'IX_cash_expenses_TenantId_SourceType_SourceId'
                         AND object_id = OBJECT_ID('cash_expenses'))
                DROP INDEX [IX_cash_expenses_TenantId_SourceType_SourceId] ON [cash_expenses];
            IF COL_LENGTH('cash_expenses', 'SourceType') IS NOT NULL
                ALTER TABLE [cash_expenses] DROP COLUMN [SourceType];
            IF COL_LENGTH('cash_expenses', 'SourceId') IS NOT NULL
                ALTER TABLE [cash_expenses] DROP COLUMN [SourceId];
            IF COL_LENGTH('cash_expenses', 'CashMovementId') IS NOT NULL
                ALTER TABLE [cash_expenses] DROP COLUMN [CashMovementId];
            """);
    }
}
