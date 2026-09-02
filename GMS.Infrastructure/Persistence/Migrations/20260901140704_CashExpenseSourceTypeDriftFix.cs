using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashExpenseSourceTypeDriftFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET QUOTED_IDENTIFIER ON;
                IF COL_LENGTH('cash_expenses', 'SourceType') IS NOT NULL
                BEGIN
                    UPDATE [cash_expenses] SET [SourceType] = 'running_cost' WHERE [SourceType] IS NULL;
                    ALTER TABLE [cash_expenses] ALTER COLUMN [SourceType] varchar(40) NULL;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''SourceType'') IS NOT NULL
                    UPDATE [cash_expenses] SET [SourceType] = ''running_cost'' WHERE [SourceType] IS NULL;');
                EXEC(N'IF COL_LENGTH(''cash_expenses'', ''SourceType'') IS NOT NULL
                    ALTER TABLE [cash_expenses] ALTER COLUMN [SourceType] varchar(40) NOT NULL;');
                """);
        }
    }
}
