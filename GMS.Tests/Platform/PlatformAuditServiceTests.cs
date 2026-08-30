namespace GMS.Tests.Platform;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Platform.Entities;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class PlatformAuditServiceTests
{
    private static PlatformAuditService CreateService(PlatformDbContext db) =>
        new(db, new HttpContextAccessor(), NullLogger<PlatformAuditService>.Instance);

    private static PlatformDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst_AcrossAllTenants()
    {
        await using var db = CreateInMemoryDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.PlatformAuditLogs.AddRange(
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "tenant.suspended", TenantId = tenantA, CreatedAtUtc = now.AddMinutes(-10) },
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "tenant.reactivated", TenantId = tenantB, CreatedAtUtc = now.AddMinutes(-1) },
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "subscription.change_tier", TenantId = tenantA, CreatedAtUtc = now.AddMinutes(-5) });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var page = await svc.ListAsync(tenantId: null, action: null, from: null, to: null, page: 1, pageSize: 20);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            new[] { "tenant.reactivated", "subscription.change_tier", "tenant.suspended" },
            page.Items.Select(i => i.Action));
    }

    [Fact]
    public async Task ListAsync_FiltersByTenantId()
    {
        await using var db = CreateInMemoryDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.PlatformAuditLogs.AddRange(
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "a", TenantId = tenantA, CreatedAtUtc = DateTime.UtcNow },
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "b", TenantId = tenantB, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var page = await svc.ListAsync(tenantId: tenantA, action: null, from: null, to: null, page: 1, pageSize: 20);

        var row = Assert.Single(page.Items);
        Assert.Equal("a", row.Action);
    }

    [Fact]
    public async Task ListAsync_FiltersByActionSubstringAndDateRange()
    {
        await using var db = CreateInMemoryDb();
        var tenantId = Guid.NewGuid();

        db.PlatformAuditLogs.AddRange(
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "tenant.suspended", TenantId = tenantId, CreatedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) },
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "tenant.reactivated", TenantId = tenantId, CreatedAtUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) },
            new PlatformAuditLog { ActorPlatformUserId = Guid.NewGuid(), Action = "subscription.change_tier", TenantId = tenantId, CreatedAtUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        var byAction = await svc.ListAsync(null, "tenant.", null, null, 1, 20);
        Assert.Equal(2, byAction.TotalCount);

        var byRange = await svc.ListAsync(null, null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 1, 20);
        Assert.Equal(2, byRange.TotalCount);
        Assert.DoesNotContain(byRange.Items, i => i.Action == "tenant.suspended");
    }

    [Fact]
    public async Task ListAsync_ResolvesActorNameFromPlatformAdminUsers()
    {
        await using var db = CreateInMemoryDb();
        var actorId = Guid.NewGuid();
        db.PlatformAdminUsers.Add(new PlatformAdminUser
        {
            Id = actorId, Email = "ops@gymflow.local", FullName = "Ops Person", PasswordHash = "x",
            Role = "platform_ops", CreatedAtUtc = DateTime.UtcNow
        });
        db.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            ActorPlatformUserId = actorId, Action = "tenant.suspended", TenantId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var page = await svc.ListAsync(null, null, null, null, 1, 20);

        Assert.Equal("Ops Person", page.Items.Single().ActorName);
    }

    [Fact]
    public async Task ListAsync_PaginatesConsistently()
    {
        await using var db = CreateInMemoryDb();
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            db.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                ActorPlatformUserId = Guid.NewGuid(), Action = $"action-{i}", TenantId = tenantId,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var page1 = await svc.ListAsync(null, null, null, null, 1, 2);
        var page2 = await svc.ListAsync(null, null, null, null, 2, 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));
    }
}
