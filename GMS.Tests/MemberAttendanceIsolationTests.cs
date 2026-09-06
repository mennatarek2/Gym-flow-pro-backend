namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Tests.Helpers;

/// <summary>
/// Member App visit history must never leak across members; soft-deleted rows stay hidden.
/// </summary>
public class MemberAttendanceIsolationTests
{
    private sealed class Fixture
    {
        public GymFlowProDbContext Ctx { get; init; } = null!;
        public MemberService Members { get; init; } = null!;
        public Guid TenantId { get; init; }
        public Guid IdentityA { get; init; }
        public Guid IdentityB { get; init; }
        public Guid MemberAId { get; init; }
        public Guid MemberBId { get; init; }
        public Guid VisitA1 { get; init; }
        public Guid VisitA2 { get; init; }
        public Guid VisitB1 { get; init; }
        public Guid SoftDeletedA { get; init; }
    }

    private static async Task<Fixture> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Attendance Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Attendance Gym",
            NameAr = "صالة",
            GymCode = $"A-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var identityA = Guid.NewGuid();
        var identityB = Guid.NewGuid();
        var appA = new AppUser
        {
            TenantId = tenantId,
            UserId = identityA.ToString(),
            Email = "a@test.local",
            FirstName = "Member",
            LastName = "A",
            Role = "Member",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var appB = new AppUser
        {
            TenantId = tenantId,
            UserId = identityB.ToString(),
            Email = "b@test.local",
            FirstName = "Member",
            LastName = "B",
            Role = "Member",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.AddRange(appA, appB);

        var memberA = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "ATT-A",
            FullName = "Member A",
            PhoneNumber = "+201000000001",
            IsActive = true,
            AppUserId = appA.Id,
            DateOfBirth = new DateOnly(1990, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        };
        var memberB = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "ATT-B",
            FullName = "Member B",
            PhoneNumber = "+201000000002",
            IsActive = true,
            AppUserId = appB.Id,
            DateOfBirth = new DateOnly(1991, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.AddRange(memberA, memberB);

        var now = DateTime.UtcNow;
        var visitA1 = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = memberA.Id,
            CheckInAtUtc = now.AddHours(-2),
            EntryMethod = "qr",
            CreatedAtUtc = now.AddHours(-2)
        };
        var visitA2 = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = memberA.Id,
            CheckInAtUtc = now.AddHours(-1),
            EntryMethod = "manual",
            CreatedAtUtc = now.AddHours(-1)
        };
        var softDeletedA = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = memberA.Id,
            CheckInAtUtc = now.AddHours(-3),
            EntryMethod = "qr",
            IsDeleted = true,
            CreatedAtUtc = now.AddHours(-3)
        };
        var visitB1 = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = memberB.Id,
            CheckInAtUtc = now.AddMinutes(-30),
            EntryMethod = "barcode",
            CreatedAtUtc = now.AddMinutes(-30)
        };
        ctx.GymAttendances.AddRange(visitA1, visitA2, softDeletedA, visitB1);
        await ctx.SaveChangesAsync();

        var members = new MemberService(
            ctx,
            new MemberRepository(ctx),
            new AesEncryptionService(new ConfigurationBuilder().Build()),
            new UnlimitedTierEnforcement(),
            new NoOpReferralAttribution(),
            new NoOpMemberAppActivation(),
            new ActivityEntitlementService(ctx),
            NullLogger<MemberService>.Instance);

        return new Fixture
        {
            Ctx = ctx,
            Members = members,
            TenantId = tenantId,
            IdentityA = identityA,
            IdentityB = identityB,
            MemberAId = memberA.Id,
            MemberBId = memberB.Id,
            VisitA1 = visitA1.Id,
            VisitA2 = visitA2.Id,
            VisitB1 = visitB1.Id,
            SoftDeletedA = softDeletedA.Id
        };
    }

    [Fact]
    public async Task GetMyAttendance_ReturnsOnlyOwnVisits_NewestFirst()
    {
        var f = await SeedAsync();

        var result = await f.Members.GetMyAttendanceAsync(f.TenantId, f.IdentityA, page: 1, pageSize: 20);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.DoesNotContain(result.Data.Items, x => x.Id == f.VisitB1);
        Assert.DoesNotContain(result.Data.Items, x => x.Id == f.SoftDeletedA);
        Assert.Equal(f.VisitA2, result.Data.Items[0].Id);
        Assert.Equal(f.VisitA1, result.Data.Items[1].Id);
    }

    [Fact]
    public async Task GetMyAttendance_MemberB_DoesNotSeeMemberA()
    {
        var f = await SeedAsync();

        var result = await f.Members.GetMyAttendanceAsync(f.TenantId, f.IdentityB, page: 1, pageSize: 20);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Data!.Items);
        Assert.Equal(f.VisitB1, result.Data.Items[0].Id);
        Assert.DoesNotContain(result.Data.Items, x => x.Id == f.VisitA1 || x.Id == f.VisitA2);
    }

    [Fact]
    public async Task GetMyAttendance_SoftDeletedExcluded()
    {
        var f = await SeedAsync();

        var result = await f.Members.GetMyAttendanceAsync(f.TenantId, f.IdentityA, page: 1, pageSize: 50);

        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain(result.Data!.Items, x => x.Id == f.SoftDeletedA);
    }

    [Fact]
    public async Task GetMyAttendance_UnlinkedIdentity_Fails()
    {
        var f = await SeedAsync();
        var orphan = Guid.NewGuid();

        var result = await f.Members.GetMyAttendanceAsync(f.TenantId, orphan, page: 1, pageSize: 20);

        Assert.False(result.IsSuccess);
        Assert.Contains("Member profile not found", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMyAttendance_PageClamp_Respected()
    {
        var f = await SeedAsync();

        var result = await f.Members.GetMyAttendanceAsync(f.TenantId, f.IdentityA, page: 0, pageSize: 999);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(50, result.Data.PageSize);
        Assert.Equal(2, result.Data.Items.Count);
    }

    [Fact]
    public async Task GetMemberAttendance_StaffPath_AlsoExcludesSoftDeleted()
    {
        var f = await SeedAsync();

        var result = await f.Members.GetMemberAttendanceAsync(f.MemberAId, page: 1, pageSize: 20);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.DoesNotContain(result.Data.Items, x => x.Id == f.SoftDeletedA);
    }
}
