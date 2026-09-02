using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollPaymentDisbursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID('payroll_payments', 'U') IS NULL
                BEGIN
                    CREATE TABLE [payroll_payments] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [DF_payroll_payments_Id] DEFAULT (NEWSEQUENTIALID()),
                        [TenantId] uniqueidentifier NOT NULL,
                        [PayrollPeriodId] uniqueidentifier NOT NULL,
                        [PayrollLineId] uniqueidentifier NOT NULL,
                        [Amount] DECIMAL(14,2) NOT NULL,
                        [PaidDate] DATE NOT NULL,
                        [PaymentMethod] VARCHAR(30) NOT NULL,
                        [Reference] NVARCHAR(200) NULL,
                        [PaidByAppUserId] uniqueidentifier NOT NULL,
                        [CashExpenseId] uniqueidentifier NOT NULL,
                        [CashMovementId] uniqueidentifier NULL,
                        [Status] VARCHAR(20) NOT NULL,
                        [CreatedAtUtc] datetime2 NOT NULL,
                        [UpdatedAtUtc] datetime2 NULL,
                        [IsDeleted] bit NOT NULL CONSTRAINT [DF_payroll_payments_IsDeleted] DEFAULT (0),
                        CONSTRAINT [PK_payroll_payments] PRIMARY KEY ([Id])
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payroll_payments_CashExpenseId' AND object_id = OBJECT_ID('payroll_payments'))
                    CREATE INDEX [IX_payroll_payments_CashExpenseId] ON [payroll_payments] ([CashExpenseId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payroll_payments_CashMovementId' AND object_id = OBJECT_ID('payroll_payments'))
                    CREATE INDEX [IX_payroll_payments_CashMovementId] ON [payroll_payments] ([CashMovementId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payroll_payments_PaidByAppUserId' AND object_id = OBJECT_ID('payroll_payments'))
                    CREATE INDEX [IX_payroll_payments_PaidByAppUserId] ON [payroll_payments] ([PaidByAppUserId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payroll_payments_PayrollLineId' AND object_id = OBJECT_ID('payroll_payments'))
                    CREATE INDEX [IX_payroll_payments_PayrollLineId] ON [payroll_payments] ([PayrollLineId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payroll_payments_PayrollPeriodId' AND object_id = OBJECT_ID('payroll_payments'))
                    CREATE INDEX [IX_payroll_payments_PayrollPeriodId] ON [payroll_payments] ([PayrollPeriodId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payroll_payments_TenantId_PayrollPeriodId_PaidDate' AND object_id = OBJECT_ID('payroll_payments'))
                    CREATE INDEX [IX_payroll_payments_TenantId_PayrollPeriodId_PaidDate]
                        ON [payroll_payments] ([TenantId], [PayrollPeriodId], [PaidDate]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_payroll_payments_tenants_TenantId')
                    ALTER TABLE [payroll_payments] ADD CONSTRAINT [FK_payroll_payments_tenants_TenantId]
                        FOREIGN KEY ([TenantId]) REFERENCES [tenants] ([Id]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_payroll_payments_payroll_periods_PayrollPeriodId')
                    ALTER TABLE [payroll_payments] ADD CONSTRAINT [FK_payroll_payments_payroll_periods_PayrollPeriodId]
                        FOREIGN KEY ([PayrollPeriodId]) REFERENCES [payroll_periods] ([Id]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_payroll_payments_payroll_lines_PayrollLineId')
                    ALTER TABLE [payroll_payments] ADD CONSTRAINT [FK_payroll_payments_payroll_lines_PayrollLineId]
                        FOREIGN KEY ([PayrollLineId]) REFERENCES [payroll_lines] ([Id]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_payroll_payments_app_users_PaidByAppUserId')
                    ALTER TABLE [payroll_payments] ADD CONSTRAINT [FK_payroll_payments_app_users_PaidByAppUserId]
                        FOREIGN KEY ([PaidByAppUserId]) REFERENCES [app_users] ([Id]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_payroll_payments_cash_expenses_CashExpenseId')
                    ALTER TABLE [payroll_payments] ADD CONSTRAINT [FK_payroll_payments_cash_expenses_CashExpenseId]
                        FOREIGN KEY ([CashExpenseId]) REFERENCES [cash_expenses] ([Id]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_payroll_payments_cash_movements_CashMovementId')
                    ALTER TABLE [payroll_payments] ADD CONSTRAINT [FK_payroll_payments_cash_movements_CashMovementId]
                        FOREIGN KEY ([CashMovementId]) REFERENCES [cash_movements] ([Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID('payroll_payments', 'U') IS NOT NULL
                    DROP TABLE [payroll_payments];
                """);
        }
    }
}
