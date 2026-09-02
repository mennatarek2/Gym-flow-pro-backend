using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashExpenseStructuredFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''Description'') IS NULL
                    ALTER TABLE [cash_expenses] ADD [Description] nvarchar(500) NULL;');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''IdempotencyKey'') IS NULL
                    ALTER TABLE [cash_expenses] ADD [IdempotencyKey] nvarchar(100) NULL;');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''Payee'') IS NULL
                    ALTER TABLE [cash_expenses] ADD [Payee] nvarchar(200) NULL;');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''PaymentMethod'') IS NULL
                    ALTER TABLE [cash_expenses] ADD [PaymentMethod] varchar(20) NOT NULL
                        CONSTRAINT [DF_cash_expenses_PaymentMethod] DEFAULT (''cash'');');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''ShiftId'') IS NULL
                    ALTER TABLE [cash_expenses] ADD [ShiftId] uniqueidentifier NULL;');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''SourceReference'') IS NULL
                    ALTER TABLE [cash_expenses] ADD [SourceReference] nvarchar(200) NULL;');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''SourceType'') IS NULL
                    ALTER TABLE [cash_expenses] ADD [SourceType] varchar(40) NULL;');
                EXEC(N'IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ''IX_cash_expenses_ShiftId'' AND object_id = OBJECT_ID(''cash_expenses''))
                    CREATE INDEX [IX_cash_expenses_ShiftId] ON [cash_expenses] ([ShiftId]);');
                EXEC(N'IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ''IX_cash_expenses_TenantId_IdempotencyKey'' AND object_id = OBJECT_ID(''cash_expenses''))
                    CREATE UNIQUE INDEX [IX_cash_expenses_TenantId_IdempotencyKey]
                        ON [cash_expenses] ([TenantId], [IdempotencyKey])
                        WHERE [IdempotencyKey] IS NOT NULL;');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''ShiftId'') IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = ''FK_cash_expenses_shifts_ShiftId'')
                    ALTER TABLE [cash_expenses] ADD CONSTRAINT [FK_cash_expenses_shifts_ShiftId]
                        FOREIGN KEY ([ShiftId]) REFERENCES [shifts] ([Id]) ON DELETE SET NULL;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_cash_expenses_shifts_ShiftId')
                    ALTER TABLE [cash_expenses] DROP CONSTRAINT [FK_cash_expenses_shifts_ShiftId];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cash_expenses_ShiftId' AND object_id = OBJECT_ID('cash_expenses'))
                    DROP INDEX [IX_cash_expenses_ShiftId] ON [cash_expenses];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cash_expenses_TenantId_IdempotencyKey' AND object_id = OBJECT_ID('cash_expenses'))
                    DROP INDEX [IX_cash_expenses_TenantId_IdempotencyKey] ON [cash_expenses];
                IF COL_LENGTH('cash_expenses', 'Description') IS NOT NULL
                    ALTER TABLE [cash_expenses] DROP COLUMN [Description];
                IF COL_LENGTH('cash_expenses', 'IdempotencyKey') IS NOT NULL
                    ALTER TABLE [cash_expenses] DROP COLUMN [IdempotencyKey];
                IF COL_LENGTH('cash_expenses', 'Payee') IS NOT NULL
                    ALTER TABLE [cash_expenses] DROP COLUMN [Payee];
                IF COL_LENGTH('cash_expenses', 'PaymentMethod') IS NOT NULL
                    ALTER TABLE [cash_expenses] DROP COLUMN [PaymentMethod];
                IF COL_LENGTH('cash_expenses', 'ShiftId') IS NOT NULL
                    ALTER TABLE [cash_expenses] DROP COLUMN [ShiftId];
                IF COL_LENGTH('cash_expenses', 'SourceReference') IS NOT NULL
                    ALTER TABLE [cash_expenses] DROP COLUMN [SourceReference];
                IF COL_LENGTH('cash_expenses', 'SourceType') IS NOT NULL
                    ALTER TABLE [cash_expenses] DROP COLUMN [SourceType];
                """);
        }
    }
}
