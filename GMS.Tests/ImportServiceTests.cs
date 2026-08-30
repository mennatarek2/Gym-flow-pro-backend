namespace GMS.Tests;

using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Tests.Helpers;

public class ImportServiceTests
{
    /// <summary>ImportService.UploadAsync/SetMappingAsync/etc. call the static Hangfire
    /// BackgroundJob.Enqueue directly (same convention as InvoiceService.EnqueueForSale) — that
    /// throws unless some JobStorage is configured for the process. An in-memory storage is enough
    /// to let the enqueue call succeed; these tests never need the job to actually run via Hangfire
    /// (ValidateAsync/ExecuteAsync are called directly instead, same as every other job test here).</summary>
    static ImportServiceTests()
    {
        Hangfire.JobStorage.Current = new Hangfire.InMemory.InMemoryStorage();
    }

    private class NoOpFileStorageService : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{Guid.NewGuid():N}-{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
    }

    private static (GymFlowProDbContext ctx, ImportService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);

        var memberService = new MemberService(
            ctx, new MemberRepository(ctx), new AesEncryptionService(new ConfigurationBuilder().Build()),
            new UnlimitedTierEnforcement(),
            new NoOpReferralAttribution(),
            new NoOpMemberAppActivation(),
            new ActivityEntitlementService(ctx),
            NullLogger<MemberService>.Instance);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);

        var svc = new ImportService(ctx, memberService, new NoOpFileStorageService(), auditService, tenantContext, new AlwaysEnabledFeatureAccess(), NullLogger<ImportService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static Tenant SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة اختبار",
            GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
            City = "Cairo",
            Address = "Test Address",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@test.local",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        return tenant;
    }

    private static (AppUser staff, Guid identityUserId) SeedStaff(GymFlowProDbContext ctx, Guid tenantId, string role = "Receptionist")
    {
        var identityUserId = Guid.NewGuid();
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{identityUserId}@test.local",
            Role = role
        };
        ctx.AppUsers.Add(staff);
        return (staff, identityUserId);
    }

    private static MembershipPlan SeedPlan(GymFlowProDbContext ctx, Guid tenantId, string name, string nameAr, decimal price = 300m)
    {
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = name,
            NameAr = nameAr,
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = price,
            IsActive = true
        };
        ctx.MembershipPlans.Add(plan);
        return plan;
    }

    [Fact]
    public async Task ValidateAsync_PlanNameFuzzyMatchesArabicName_ResolvesToPlan()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedPlan(ctx, tenantId, "Monthly Plan", "الخطة الشهرية");
        await ctx.SaveChangesAsync();

        var csv = "Name,Phone,Plan,Start Date,End Date\n" +
                   $"Ahmed Ali,01001234567,الخطة الشهرية,{DateTime.UtcNow:yyyy-MM-dd},{DateTime.UtcNow.AddDays(30):yyyy-MM-dd}\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var uploadResult = await svc.UploadAsync(stream, "members.csv", "text/csv", identityUserId, tenantId);
        Assert.True(uploadResult.IsSuccess, uploadResult.Error);

        await svc.ValidateAsync(uploadResult.Data!.Id, tenantId);

        var row = await ctx.ImportRows.SingleAsync(r => r.BatchId == uploadResult.Data.Id);
        Assert.Equal("ok", row.Status);
        Assert.Null(row.ErrorCodes);
    }

    [Fact]
    public async Task ValidateAsync_UnmatchablePlanName_MarksRowPlanUnmatched()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedPlan(ctx, tenantId, "Monthly Plan", "الخطة الشهرية");
        await ctx.SaveChangesAsync();

        var csv = "Name,Phone,Plan,Start Date,End Date\n" +
                   $"Ahmed Ali,01001234567,xyz,{DateTime.UtcNow:yyyy-MM-dd},{DateTime.UtcNow.AddDays(30):yyyy-MM-dd}\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var uploadResult = await svc.UploadAsync(stream, "members.csv", "text/csv", identityUserId, tenantId);
        Assert.True(uploadResult.IsSuccess, uploadResult.Error);

        await svc.ValidateAsync(uploadResult.Data!.Id, tenantId);

        var row = await ctx.ImportRows.SingleAsync(r => r.BatchId == uploadResult.Data.Id);
        Assert.Equal("error", row.Status);
        Assert.Contains(ImportRowErrorCodes.PlanUnmatched, row.ErrorCodes);
    }

    [Fact]
    public async Task FullPipeline_OneThousandRowFixture_AllRowsImportedWithNoErrors()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        SeedPlan(ctx, tenantId, "Monthly Unlimited", "شهري بدون حدود");
        await ctx.SaveChangesAsync();

        const int rowCount = 1000;
        var start = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var end = DateTime.UtcNow.AddDays(365).ToString("yyyy-MM-dd");

        var sb = new StringBuilder();
        sb.AppendLine("Name,Phone,Plan,Start Date,End Date");
        for (var i = 0; i < rowCount; i++)
            sb.AppendLine($"Member {i},010{i:D8},Monthly Unlimited,{start},{end}");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        var uploadResult = await svc.UploadAsync(stream, "members.csv", "text/csv", identityUserId, tenantId);
        Assert.True(uploadResult.IsSuccess, uploadResult.Error);
        Assert.Equal(rowCount, uploadResult.Data!.TotalRows);

        await svc.ValidateAsync(uploadResult.Data.Id, tenantId);

        var validated = await svc.GetAsync(uploadResult.Data.Id, tenantId);
        Assert.Equal("dry_run_ready", validated!.Status);
        Assert.Equal(rowCount, validated.OkRows);
        Assert.Equal(0, validated.ErrorRows);

        await svc.ExecuteAsync(uploadResult.Data.Id, tenantId);

        var completed = await svc.GetAsync(uploadResult.Data.Id, tenantId);
        Assert.Equal("completed", completed!.Status);

        var importedRowCount = await ctx.ImportRows
            .CountAsync(r => r.BatchId == uploadResult.Data.Id && r.Status == "imported");
        Assert.Equal(rowCount, importedRowCount);

        var memberCount = await ctx.GymMembers.CountAsync(m => m.TenantId == tenantId);
        Assert.Equal(rowCount, memberCount);

        var membershipCount = await ctx.Memberships.CountAsync(m => m.TenantId == tenantId);
        Assert.Equal(rowCount, membershipCount);
    }

    [Fact]
    public async Task RollbackAsync_OneMemberHasAttendance_ThatMemberRetainedOthersDeleted()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);
        var (manager, managerIdentityId) = SeedStaff(ctx, tenantId, role: "Manager");
        var plan = SeedPlan(ctx, tenantId, "Monthly Unlimited", "شهري بدون حدود");
        await ctx.SaveChangesAsync();

        var batch = new ImportBatch
        {
            TenantId = tenantId,
            UploadedByUserId = staff.Id,
            FileName = "members.csv",
            FileBlobUrl = "/uploads/imports/members.csv",
            EntityScope = "members_memberships",
            Status = "completed",
            TotalRows = 2,
            OkRows = 2,
            ErrorRows = 0,
            CompletedAt = DateTime.UtcNow
        };
        ctx.ImportBatches.Add(batch);

        // Member A: no post-import activity — should be rolled back (deleted).
        var memberA = new GymMember
        {
            TenantId = tenantId, MemberNumber = "GYM-A01", FullName = "Member A", FullNameAr = "عضو أ",
            PhoneNumber = "+201000000001", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20))
        };
        ctx.GymMembers.Add(memberA);
        var membershipA = new Membership
        {
            TenantId = tenantId, MemberId = memberA.Id, PlanId = plan.Id,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = "active", PaymentMethod = "imported"
        };
        ctx.Memberships.Add(membershipA);

        // Member B: HAS a post-import attendance record — must be retained.
        var memberB = new GymMember
        {
            TenantId = tenantId, MemberNumber = "GYM-B01", FullName = "Member B", FullNameAr = "عضو ب",
            PhoneNumber = "+201000000002", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20))
        };
        ctx.GymMembers.Add(memberB);
        var membershipB = new Membership
        {
            TenantId = tenantId, MemberId = memberB.Id, PlanId = plan.Id,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = "active", PaymentMethod = "imported"
        };
        ctx.Memberships.Add(membershipB);
        await ctx.SaveChangesAsync();

        ctx.GymAttendances.Add(new GymAttendance
        {
            TenantId = tenantId, MemberId = memberB.Id, MembershipId = membershipB.Id,
            CheckInAtUtc = DateTime.UtcNow, EntryMethod = "manual"
        });
        await ctx.SaveChangesAsync();

        ctx.ImportRows.Add(new ImportRow
        {
            TenantId = tenantId, BatchId = batch.Id, RowNumber = 1,
            RawJson = "{}", Status = "imported", CreatedMemberId = memberA.Id, CreatedMembershipId = membershipA.Id
        });
        ctx.ImportRows.Add(new ImportRow
        {
            TenantId = tenantId, BatchId = batch.Id, RowNumber = 2,
            RawJson = "{}", Status = "imported", CreatedMemberId = memberB.Id, CreatedMembershipId = membershipB.Id
        });
        await ctx.SaveChangesAsync();

        var rollbackResult = await svc.RollbackAsync(batch.Id, managerIdentityId, tenantId);
        Assert.True(rollbackResult.IsSuccess, rollbackResult.Error);
        Assert.Equal("rolled_back", rollbackResult.Data!.Status);

        Assert.False(await ctx.GymMembers.AnyAsync(m => m.Id == memberA.Id));
        Assert.True(await ctx.GymMembers.AnyAsync(m => m.Id == memberB.Id));

        var rowA = await ctx.ImportRows.SingleAsync(r => r.CreatedMemberId == null && r.RowNumber == 1 && r.BatchId == batch.Id);
        Assert.Contains(ImportRowErrorCodes.RolledBack, rowA.ErrorCodes);

        var rowB = await ctx.ImportRows.SingleAsync(r => r.RowNumber == 2 && r.BatchId == batch.Id);
        Assert.Contains(ImportRowErrorCodes.RetainedHasActivity, rowB.ErrorCodes);
        Assert.NotNull(rowB.CreatedMemberId);

        // Re-executing a rolled-back batch must be rejected (status is no longer dry_run_ready).
        var reExecuteResult = await svc.EnqueueExecuteAsync(batch.Id, tenantId);
        Assert.False(reExecuteResult.IsSuccess);
        Assert.StartsWith(ImportFailureReasons.InvalidStatus + "|", reExecuteResult.Error);
    }
}
