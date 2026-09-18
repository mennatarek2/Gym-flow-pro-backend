using Microsoft.Data.SqlClient;

namespace GMS.Infrastructure.Configuration;

/// <summary>
/// SSMS Delete Object sets SINGLE_USER then DROP. If DROP fails because HyMotion is connected,
/// the gym database stays SINGLE_USER: Object Explorer shows "not accessible", login returns
/// generic Request failed, and a second GMS.Api cannot connect. High availability requires
/// MULTI_USER. Best-effort — never throws (startup continues to migrations).
/// </summary>
public static class LocalDatabaseAccessRepair
{
    public static void TryReleaseSingleUser(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            var parsed = new SqlConnectionStringBuilder(connectionString);
            var database = parsed.InitialCatalog;
            if (string.IsNullOrWhiteSpace(database)) return;

            parsed.InitialCatalog = "master";
            parsed.ConnectTimeout = Math.Max(parsed.ConnectTimeout, 8);

            using var conn = new SqlConnection(parsed.ConnectionString);
            conn.Open();

            using var lookup = conn.CreateCommand();
            lookup.CommandText = "SELECT user_access_desc FROM sys.databases WHERE name = @n";
            lookup.Parameters.AddWithValue("@n", database);
            var access = lookup.ExecuteScalar() as string;
            if (!string.Equals(access, "SINGLE_USER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(access, "RESTRICTED_USER", StringComparison.OrdinalIgnoreCase))
                return;

            var escaped = database.Replace("]", "]]", StringComparison.Ordinal);
            using var alter = conn.CreateCommand();
            alter.CommandTimeout = 30;
            alter.CommandText = "ALTER DATABASE [" + escaped + "] SET MULTI_USER WITH ROLLBACK IMMEDIATE";
            alter.ExecuteNonQuery();
        }
        catch
        {
            // Startup still attempts migrations; login may fail until MULTI_USER is set.
        }
    }
}
