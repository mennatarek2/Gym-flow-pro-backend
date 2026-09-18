namespace GMS.Tests.Platform;

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using GMS.Core.Licensing;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

/// <summary>
/// 2.1-H: DeviceLimit must hold under concurrent Activate calls for the same license.
/// This test talks to SQL Server LocalDB so UPDLOCK is real. It does not use EF InMemory.
/// </summary>
public class LocalLicenseActivationConcurrencyTests
{
    private static readonly string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_LicenseActivationConcurrency;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public async Task ActivateAsync_ConcurrentDifferentInstallations_HonorsDeviceLimit()
    {
        var options = SqlOptions();
        PlatformDbContext setup;
        try
        {
            setup = await CreateRelationalDbAsync(options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SKIPPED: SQL Server LocalDB is not available ({ex.GetType().Name}: {ex.Message} | inner: {ex.InnerException?.Message}).");
            return;
        }

        await using (setup)
        {
            var customer = new PlatformCustomer { BusinessName = "Concurrency Gym", OwnerName = "Owner" };
            setup.Customers.Add(customer);
            await setup.SaveChangesAsync();

            var signer = NewSigner();
            var issuer = new LocalLicenseService(new LocalLicenseWriteRepository(setup), signer, setup);
            var license = await issuer.IssueAsync(new IssueLocalLicenseRequest { CustomerId = customer.Id, DeviceLimit = 1 }, Guid.NewGuid());
            setup.ChangeTracker.Clear();

            await using var db1 = new PlatformDbContext(options);
            await using var db2 = new PlatformDbContext(options);
            var svc1 = new LocalLicenseService(new LocalLicenseWriteRepository(db1), signer, db1);
            var svc2 = new LocalLicenseService(new LocalLicenseWriteRepository(db2), signer, db2);

            var first = svc1.ActivateAsync(license.LicenseKey, "INS-A", null, "1.1.1.1");
            var second = svc2.ActivateAsync(license.LicenseKey, "INS-B", null, "2.2.2.2");
            var results = await Task.WhenAll(first, second);

            Assert.Equal(1, results.Count(r => r.Success));
            Assert.Equal(1, results.Count(r => r.Result == LocalActivationResults.DeviceLimitExceeded));

            await using var verify = new PlatformDbContext(options);
            var active = await verify.LocalInstallations.CountAsync(i =>
                i.LicenseId == license.Id && i.Status == LocalInstallationStatuses.Active);
            Assert.Equal(1, active);
        }
    }

    private static DbContextOptions<PlatformDbContext> SqlOptions() =>
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDb, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;

    private static async Task<PlatformDbContext> CreateRelationalDbAsync(DbContextOptions<PlatformDbContext> options)
    {
        var db = new PlatformDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID('dbo.tenants', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tenants (
        Id uniqueidentifier NOT NULL PRIMARY KEY
    );
END
""");
        await db.Database.MigrateAsync();
        return db;
    }

    private static ILicenseSigningService NewSigner()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LicenseSigning:PrivateKeyPem"] = ec.ExportECPrivateKeyPem(),
            })
            .Build();
        return new LicenseSigningService(config);
    }
}
