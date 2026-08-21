using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace GMS.Platform.Persistence;

/// <summary>
/// When a database was populated via manual SQL import, EF migration history may be empty while
/// tables already exist. Retry migrations by marking conflicting steps as applied (SQL error 2714).
/// </summary>
public static class MigrationImportBaselineExtensions
{
    private const string EfProductVersion = "8.0.10";
    private const int MaxBaselineAttempts = 64;

    public static async Task MigrateWithSqlImportBaselineAsync(
        this DatabaseFacade database,
        ILogger logger,
        string historySchema,
        string historyTable,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxBaselineAttempts; attempt++)
        {
            try
            {
                await database.MigrateAsync(cancellationToken);
                return;
            }
            catch (SqlException ex) when (ex.Number is 2714 or 1801)
            {
                // 2714 = object already exists, 1801 = database already exists.
                // These are recoverable when a prior SQL import created the objects
                // but left the EF migration history empty.
                // Other errors (e.g. 1767 = FK references invalid table) are NOT
                // recoverable and must propagate.
                var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count == 0)
                    throw;

                var migrationId = pending[0];
                logger.LogWarning(
                    ex,
                    "Migration {MigrationId} conflicted with existing objects (manual SQL import). Marking as applied in {Schema}.{Table}.",
                    migrationId,
                    historySchema,
                    historyTable);

                await MarkMigrationAppliedAsync(
                    database,
                    historySchema,
                    historyTable,
                    migrationId,
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Database migration baseline exceeded {MaxBaselineAttempts} attempts. Check schema vs migration history.");
    }

    private static async Task MarkMigrationAppliedAsync(
        DatabaseFacade database,
        string historySchema,
        string historyTable,
        string migrationId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            IF NOT EXISTS (
                SELECT 1 FROM [{historySchema}].[{historyTable}]
                WHERE [MigrationId] = @migrationId)
            INSERT INTO [{historySchema}].[{historyTable}] ([MigrationId], [ProductVersion])
            VALUES (@migrationId, @productVersion)
            """;

        await database.ExecuteSqlRawAsync(
            sql,
            new SqlParameter("@migrationId", migrationId),
            new SqlParameter("@productVersion", EfProductVersion),
            cancellationToken);
    }
}
