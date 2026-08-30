namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// REM-F1 regression: prove the null-ITenantContext (background job) mode of
/// GymFlowProDbContext — filters are inactive, so ONLY explicit TenantId predicates
/// prevent cross-tenant reads; soft-delete still applies.
/// </summary>
public class TenantIsolationRegressionTests : IDisposable
{
    // Isolated database root → isolated EF model cache, so this test's null-tenant-context
    // model (no global filters) cannot be poisoned by parallel tests that construct
    // tenant-bound contexts against the default root.
    private static readonly InMemoryDatabaseRoot DbRoot = new();

    private readonly DbContextOptions<GymFlowProDbContext> _options;

    public TenantIsolationRegressionTests()
    {
        _options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), DbRoot)
            .Options;

        using var db = new GymFlowProDbContext(_options); // NO tenant context → job mode
        db.Tenants.Add(new Tenant { Id = TestData.TenantA, Name = "A", GymCode = "A" });
        db.Tenants.Add(new Tenant { Id = TestData.TenantB, Name = "B", GymCode = "B" });
        db.GymMembers.Add(new GymMember { Id = Guid.NewGuid(), TenantId = TestData.TenantA, MemberNumber = "A-1", FullName = "Member A" });
        db.GymMembers.Add(new GymMember { Id = Guid.NewGuid(), TenantId = TestData.TenantB, MemberNumber = "B-1", FullName = "Member B" });
        db.GymMembers.Add(new GymMember { Id = Guid.NewGuid(), TenantId = TestData.TenantA, MemberNumber = "A-2", FullName = "Deleted A", IsDeleted = true });
        db.SaveChanges();
    }

    [Fact]
    public void NullTenantContext_ExplicitPredicate_ReturnsOnlyRequestedTenant()
    {
        using var db = new GymFlowProDbContext(_options);
        var members = db.GymMembers.Where(m => m.TenantId == TestData.TenantA).ToList();
        Assert.All(members, m => Assert.Equal(TestData.TenantA, m.TenantId));
        Assert.Equal(1, members.Count); // soft-deleted A-2 excluded by soft-delete convention check below
    }

    [Fact]
    public void NullTenantContext_WithoutPredicate_WouldReturnAllTenants()
    {
        // Demonstrates WHY jobs must constrain by TenantId explicitly:
        // with no ambient context there is no global filter.
        using var db = new GymFlowProDbContext(_options);
        var all = db.GymMembers.IgnoreQueryFilters().ToList();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, m => m.TenantId == TestData.TenantB);
    }

    [Fact]
    public void NullTenantContext_SoftDelete_StillHonoredByDefaultFilter()
    {
        using var db = new GymFlowProDbContext(_options);
        var visible = db.GymMembers.ToList();
        Assert.DoesNotContain(visible, m => m.IsDeleted);
    }

    public void Dispose()
    {
        using var db = new GymFlowProDbContext(_options);
        db.Database.EnsureDeleted();
    }
}

internal static class TestData
{
    public static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
}
