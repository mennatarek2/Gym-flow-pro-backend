using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using GMS.Infrastructure.Persistence;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(GymFlowProDbContext))]
[Migration("20260831190000_AddSaleLineCostSnapshots")]
public partial class AddSaleLineCostSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('sale_lines', 'CogsAmount') IS NULL
                ALTER TABLE [sale_lines] ADD [CogsAmount] DECIMAL(14,2) NULL;
            IF COL_LENGTH('sale_lines', 'UnitCost') IS NULL
                ALTER TABLE [sale_lines] ADD [UnitCost] DECIMAL(12,2) NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('sale_lines', 'CogsAmount') IS NOT NULL
                ALTER TABLE [sale_lines] DROP COLUMN [CogsAmount];
            IF COL_LENGTH('sale_lines', 'UnitCost') IS NOT NULL
                ALTER TABLE [sale_lines] DROP COLUMN [UnitCost];
            """);
    }
}
