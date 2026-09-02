namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using GMS.Infrastructure.Persistence;

public sealed class MigrationReproducibilityTests
{
    [Fact]
    public async Task FreshDatabaseContainsFinancialSchemaFromRepositoryMigrations()
    {
        var databaseName = $"GymFlowProMigrationProof_{Guid.NewGuid():N}";
        var connection = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=true;Encrypt=false;";
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(connection)
            .Options;

        await using var db = new GymFlowProDbContext(options);
        try
        {
            await db.Database.MigrateAsync();

            Assert.True(await HasColumnAsync(db, "sale_lines", "CogsAmount"));
            Assert.True(await HasColumnAsync(db, "sale_lines", "UnitCost"));
            Assert.True(await HasColumnAsync(db, "payment_transactions", "SettlementStatus"));
            Assert.True(await HasTableAsync(db, "payroll_payments"));
            Assert.True(await HasTableAsync(db, "sale_adjustments"));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<bool> HasTableAsync(GymFlowProDbContext db, string table)
    {
        var rows = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = {0}",
            table).ToListAsync();
        return rows.Single() == 1;
    }

    private static async Task<bool> HasColumnAsync(
        GymFlowProDbContext db, string table, string column)
    {
        var rows = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = {0} AND COLUMN_NAME = {1}",
            table, column).ToListAsync();
        return rows.Single() == 1;
    }
}
