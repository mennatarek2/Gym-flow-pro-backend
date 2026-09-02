using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierLedgerEffectiveDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('supplier_ledger_entries', 'EffectiveAtUtc') IS NULL
                    ALTER TABLE [supplier_ledger_entries] ADD [EffectiveAtUtc] DATETIME2 NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('supplier_ledger_entries', 'EffectiveAtUtc') IS NOT NULL
                    ALTER TABLE [supplier_ledger_entries] DROP COLUMN [EffectiveAtUtc];
                """);
        }
    }
}
