namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

/// <summary>
/// Regression coverage for a bug where GymFlowProDbContext.ApplySoftDeleteFilter's per-entity
/// HasQueryFilter call silently replaced the tenant filter set by ApplyGlobalQueryFilters
/// (EF Core does not merge multiple HasQueryFilter calls on the same entity type), making
/// tenant isolation a no-op at runtime for every tenant-scoped entity.
///
/// IMPORTANT: EF Core caches the compiled model per DbContext CLR type (not per instance), so every
/// test here must construct its GymFlowProDbContext with a real, non-null ITenantContext from the
/// very first instantiation onward — exactly like production DI does (GymFlowProDbContext is always
/// resolved through the container, which always supplies a real TenantContext, never a literal null).
/// Constructing even one instance with a null tenant context anywhere in this process would permanently
/// poison the shared model cache for every other test. Cross-tenant/soft-deleted seed rows are inserted
/// through the SAME already-constructed context via IgnoreQueryFilters(), never through a second,
/// separately-constructed context.
/// </summary>
public class TenantQueryFilterTests
{
    private static GymFlowProDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        return new GymFlowProDbContext(options, tenantContext);
    }

    [Fact]
    public async Task GymMember_QueryFilter_CombinesTenantIsolationAndSoftDelete()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var ctx = CreateContext(tenantA);

        // Seed cross-tenant + soft-deleted rows via the SAME context — Add() is never subject to query filters.
        ctx.GymMembers.Add(new GymMember { TenantId = tenantA, FullName = "A-Active", MemberNumber = "A1" });
        ctx.GymMembers.Add(new GymMember { TenantId = tenantA, FullName = "A-Deleted", MemberNumber = "A2", IsDeleted = true });
        ctx.GymMembers.Add(new GymMember { TenantId = tenantB, FullName = "B-Active", MemberNumber = "B1" });
        await ctx.SaveChangesAsync();

        var visible = await ctx.GymMembers.Select(m => m.FullName).ToListAsync();

        Assert.Equal(new[] { "A-Active" }, visible);
    }

    [Fact]
    public async Task AuditEvent_QueryFilter_IsolatesByTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var ctx = CreateContext(tenantA);

        ctx.AuditEvents.Add(new AuditEvent { TenantId = tenantA, Action = "checkin.manual" });
        ctx.AuditEvents.Add(new AuditEvent { TenantId = tenantB, Action = "checkin.manual" });
        await ctx.SaveChangesAsync();

        var visible = await ctx.AuditEvents.ToListAsync();

        Assert.Single(visible);
        Assert.Equal(tenantA, visible[0].TenantId);
    }

    [Fact]
    public async Task PaymentTransaction_QueryFilter_IsolatesByTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var ctx = CreateContext(tenantA);

        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantA,
            MemberId = Guid.NewGuid(),
            MembershipId = Guid.NewGuid(),
            Gateway = "cash",
            ExternalRef = "A-1",
            Amount = 100,
            PaidAtUtc = DateTime.UtcNow
        });
        ctx.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantB,
            MemberId = Guid.NewGuid(),
            MembershipId = Guid.NewGuid(),
            Gateway = "cash",
            ExternalRef = "B-1",
            Amount = 200,
            PaidAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var visible = await ctx.PaymentTransactions.ToListAsync();
        Assert.Single(visible);
        Assert.Equal("A-1", visible[0].ExternalRef);
    }

    [Fact]
    public async Task GymAnalyticsSnapshot_QueryFilter_IsolatesByTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var ctx = CreateContext(tenantA);

        ctx.GymAnalyticsSnapshots.Add(new GymAnalyticsSnapshot
        {
            TenantId = tenantA,
            SnapshotDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });
        ctx.GymAnalyticsSnapshots.Add(new GymAnalyticsSnapshot
        {
            TenantId = tenantB,
            SnapshotDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });
        await ctx.SaveChangesAsync();

        var visible = await ctx.GymAnalyticsSnapshots.ToListAsync();
        Assert.Single(visible);
        Assert.Equal(tenantA, visible[0].TenantId);
    }
}
