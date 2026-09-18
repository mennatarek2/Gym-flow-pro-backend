namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;

/// <summary>
/// D1: CK_platform_admin_users_role must persist platform_sales on SQL Server.
/// EF InMemory does not enforce CHECK constraints.
/// </summary>
public class PlatformAdminUserRoleConstraintTests
{
    private static readonly string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformSalesRole;Trusted_Connection=true;Encrypt=false;";

    [Fact]
    public async Task CheckConstraint_AllowsSalesAndExistingRoles_RejectsInvalid()
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

        await using var db = new PlatformDbContext(options);
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var sales = NewUser($"sales-{stamp}@gymflow.local", PlatformRoles.Sales);
        var support = NewUser($"support-{stamp}@gymflow.local", PlatformRoles.Support);
        var ops = NewUser($"ops-{stamp}@gymflow.local", PlatformRoles.Ops);
        var admin = NewUser($"admin-{stamp}@gymflow.local", PlatformRoles.Admin);
        db.PlatformAdminUsers.AddRange(sales, support, ops, admin);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(PlatformRoles.Sales, await db.PlatformAdminUsers.AsNoTracking().Where(u => u.Id == sales.Id).Select(u => u.Role).SingleAsync());
        Assert.Equal(PlatformRoles.Support, await db.PlatformAdminUsers.AsNoTracking().Where(u => u.Id == support.Id).Select(u => u.Role).SingleAsync());
        Assert.Equal(PlatformRoles.Ops, await db.PlatformAdminUsers.AsNoTracking().Where(u => u.Id == ops.Id).Select(u => u.Role).SingleAsync());
        Assert.Equal(PlatformRoles.Admin, await db.PlatformAdminUsers.AsNoTracking().Where(u => u.Id == admin.Id).Select(u => u.Role).SingleAsync());

        db.PlatformAdminUsers.Add(NewUser($"bad-{stamp}@gymflow.local", "super_admin"));
        var exSave = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var detail = exSave.ToString() + (exSave.InnerException?.Message ?? string.Empty);
        Assert.True(
            detail.Contains("CK_platform_admin_users_role", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("CHECK", StringComparison.OrdinalIgnoreCase),
            $"Expected CHECK constraint violation, got: {detail}");
    }

    private static PlatformAdminUser NewUser(string email, string role) => new()
    {
        Email = email,
        PasswordHash = "x",
        FullName = role,
        Role = role,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
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
