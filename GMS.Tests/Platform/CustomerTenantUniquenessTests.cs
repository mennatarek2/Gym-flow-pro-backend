namespace GMS.Tests.Platform;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using GMS.Platform;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;

/// <summary>
/// D3: optional unique TenantId — many NULLs allowed, at most one non-null TenantId per tenant.
/// Proved on SQL Server because EF InMemory does not enforce unique filtered indexes.
/// </summary>
public class CustomerTenantUniquenessTests
{
    private static readonly string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_CustomerTenantUniqueness;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public async Task UniqueFilteredIndex_AllowsNulls_RejectsDuplicateTenantId_AndReleasesOnUnlink()
    {
        DbContextOptions<PlatformDbContext> options;
        try
        {
            options = await EnsureMigratedAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SKIPPED: SQL Server LocalDB is not available ({ex.GetType().Name}: {ex.Message} | inner: {ex.InnerException?.Message}).");
            return;
        }

        var tenantX = Guid.NewGuid();
        var unknownTenant = Guid.NewGuid();
        await using var db = new PlatformDbContext(options);

        await db.Database.ExecuteSqlInterpolatedAsync($@"
IF NOT EXISTS (SELECT 1 FROM dbo.tenants WHERE Id = {tenantX})
INSERT INTO dbo.tenants (Id) VALUES ({tenantX});
");

        try
        {
            var a = NewCustomer("Customer A");
            var b = NewCustomer("Customer B");
            var c = NewCustomer("Customer C");
            db.Customers.AddRange(a, b, c);
            await db.SaveChangesAsync();

            a.TenantId = tenantX;
            await db.SaveChangesAsync();

            b.TenantId = null;
            c.TenantId = null;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            Assert.Equal(tenantX, await db.Customers.AsNoTracking().Where(x => x.Id == a.Id).Select(x => x.TenantId).SingleAsync());
            Assert.Null(await db.Customers.AsNoTracking().Where(x => x.Id == b.Id).Select(x => x.TenantId).SingleAsync());
            Assert.Null(await db.Customers.AsNoTracking().Where(x => x.Id == c.Id).Select(x => x.TenantId).SingleAsync());

            var trackedB = await db.Customers.SingleAsync(x => x.Id == b.Id);
            trackedB.TenantId = tenantX;
            var duplicate = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var detail = duplicate.ToString() + (duplicate.InnerException?.Message ?? string.Empty);
            Assert.True(
                detail.Contains("UX_customers_TenantId", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("unique", StringComparison.OrdinalIgnoreCase),
                $"Expected unique index violation, got: {detail}");
            db.ChangeTracker.Clear();

            var trackedA = await db.Customers.SingleAsync(x => x.Id == a.Id);
            trackedA.TenantId = null;
            await db.SaveChangesAsync();

            var trackedB2 = await db.Customers.SingleAsync(x => x.Id == b.Id);
            trackedB2.TenantId = tenantX;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(tenantX, await db.Customers.AsNoTracking().Where(x => x.Id == b.Id).Select(x => x.TenantId).SingleAsync());
            Assert.Null(await db.Customers.AsNoTracking().Where(x => x.Id == a.Id).Select(x => x.TenantId).SingleAsync());

            db.Customers.Add(new PlatformCustomer
            {
                BusinessName = "Orphan Link",
                OwnerName = "Owner",
                TenantId = unknownTenant,
            });
            var fk = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(
                fk.InnerException is SqlException sql && (sql.Number == 547 || sql.Message.Contains("FK_customers_tenants_TenantId", StringComparison.OrdinalIgnoreCase))
                || (fk.ToString().Contains("FK_customers_tenants_TenantId", StringComparison.OrdinalIgnoreCase)
                    || fk.ToString().Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)),
                $"Expected FK violation, got: {fk}");
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM platform.customers WHERE TenantId = {tenantX} OR BusinessName IN ('Customer A','Customer B','Customer C','Orphan Link');
DELETE FROM dbo.tenants WHERE Id = {tenantX};
");
        }
    }

    private static PlatformCustomer NewCustomer(string name) => new()
    {
        BusinessName = name,
        OwnerName = "Owner",
        PreferredContactMethod = "whatsapp",
        Status = "prospect",
        OwnerAccountStatus = "unknown",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static async Task<DbContextOptions<PlatformDbContext>> EnsureMigratedAsync()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDb, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;

        await using var db = new PlatformDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID('dbo.tenants', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tenants (
        Id uniqueidentifier NOT NULL PRIMARY KEY
    );
END
""");
        await db.Database.MigrateAsync();
        return options;
    }
}
