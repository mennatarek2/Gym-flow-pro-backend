namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class ShiftServiceTests
{
    private static (GymFlowProDbContext ctx, ShiftService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var svc = new ShiftService(ctx, auditService, NullLogger<ShiftService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static Tenant SeedTenant(GymFlowProDbContext ctx, Guid tenantId, string? settingsJson = null)
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
            SubscriptionStartDate = DateTime.UtcNow,
            Settings = settingsJson
        };
        ctx.Tenants.Add(tenant);
        return tenant;
    }

    private static (AppUser staff, Guid identityUserId) SeedStaff(GymFlowProDbContext ctx, Guid tenantId)
    {
        var identityUserId = Guid.NewGuid();
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{identityUserId}@test.local",
            Role = "Receptionist"
        };
        ctx.AppUsers.Add(staff);
        return (staff, identityUserId);
    }

    [Fact]
    public async Task OpenAsync_SecondOpenShiftForSameUser_IsRejected()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var first = await svc.OpenAsync(100m, identityUserId, tenantId);
        Assert.True(first.IsSuccess, first.Error);

        var second = await svc.OpenAsync(200m, identityUserId, tenantId);

        Assert.False(second.IsSuccess);
        Assert.StartsWith(ShiftFailureReasons.ShiftAlreadyOpen + "|", second.Error);
    }

    [Fact]
    public async Task GetCurrentAsync_WhileOpen_NeverRevealsExpectedCash()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        await svc.OpenAsync(100m, identityUserId, tenantId);

        var current = await svc.GetCurrentAsync(identityUserId, tenantId);

        Assert.True(current.IsSuccess);
        Assert.NotNull(current.Data);
        Assert.Equal("open", current.Data!.Status);
        Assert.Null(current.Data.ExpectedCash);
    }

    [Fact]
    public async Task GetCurrentAsync_NoOpenShift_ReturnsSuccessWithNullData()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var result = await svc.GetCurrentAsync(identityUserId, tenantId);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CloseAsync_PopulatesExpectedCash_WhichWasHiddenWhileOpen()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        await svc.OpenAsync(100m, identityUserId, tenantId);

        var closeResult = await svc.CloseAsync(100m, null, identityUserId, tenantId);

        Assert.True(closeResult.IsSuccess, closeResult.Error);
        Assert.NotNull(closeResult.Data!.ExpectedCash);
        Assert.Equal(100m, closeResult.Data.ExpectedCash);
    }

    [Fact]
    public async Task CloseAsync_VarianceWithinTolerance_AutoApproves()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId, settingsJson: "{\"variance_tolerance_egp\":20}");
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        await svc.OpenAsync(100m, identityUserId, tenantId);

        // Expected = 100, counted = 110 -> variance = 10, within tolerance 20.
        var result = await svc.CloseAsync(110m, null, identityUserId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("approved", result.Data!.Status);
        Assert.Equal(10m, result.Data.Variance);
    }

    [Fact]
    public async Task CloseAsync_VarianceOutsideTolerance_NeedsApproval()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId, settingsJson: "{\"variance_tolerance_egp\":20}");
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        await svc.OpenAsync(100m, identityUserId, tenantId);

        // Expected = 100, counted = 200 -> variance = 100, outside tolerance 20.
        var result = await svc.CloseAsync(200m, "large unexplained difference", identityUserId, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("closed", result.Data!.Status);
        Assert.Equal(100m, result.Data.Variance);
    }

    [Fact]
    public async Task ApproveAsync_ClosedShift_MarksApproved()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId, settingsJson: "{\"variance_tolerance_egp\":20}");
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        var (approver, approverIdentityId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var openResult = await svc.OpenAsync(100m, identityUserId, tenantId);
        await svc.CloseAsync(200m, "big diff", identityUserId, tenantId);

        var approveResult = await svc.ApproveAsync(openResult.Data!.Id, "reviewed", approverIdentityId, tenantId);

        Assert.True(approveResult.IsSuccess, approveResult.Error);
        Assert.Equal("approved", approveResult.Data!.Status);
        Assert.Equal(approver.Id, approveResult.Data.ApprovedByUserId);
    }

    [Fact]
    public async Task ForceCloseAsync_OpenShift_ClosesWithNullCountedCashAndVariance()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (_, identityUserId) = SeedStaff(ctx, tenantId);
        var (_, managerIdentityId) = SeedStaff(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var openResult = await svc.OpenAsync(100m, identityUserId, tenantId);

        var forceCloseResult = await svc.ForceCloseAsync(openResult.Data!.Id, managerIdentityId, tenantId);

        Assert.True(forceCloseResult.IsSuccess, forceCloseResult.Error);
        Assert.Equal("closed", forceCloseResult.Data!.Status);
        Assert.Null(forceCloseResult.Data.CountedCash);
        Assert.Null(forceCloseResult.Data.Variance);
        Assert.Equal(100m, forceCloseResult.Data.ExpectedCash);
    }

    /// <summary>
    /// Property-based: for many random sequences of cash movements, CloseAsync's computed
    /// ExpectedCash must always equal OpeningFloat + the sum of the (correctly signed) amounts.
    /// </summary>
    [Fact]
    public async Task CloseAsync_RandomMovementSequences_ExpectedCashAlwaysEqualsFloatPlusMovementSum()
    {
        var random = new Random(20260712);

        for (var iteration = 0; iteration < 25; iteration++)
        {
            var (ctx, svc, tenantId) = CreateSut();
            SeedTenant(ctx, tenantId);
            var (_, identityUserId) = SeedStaff(ctx, tenantId);
            await ctx.SaveChangesAsync();

            var openingFloat = Math.Round((decimal)(random.NextDouble() * 500), 2);
            var openResult = await svc.OpenAsync(openingFloat, identityUserId, tenantId);
            Assert.True(openResult.IsSuccess, openResult.Error);
            var shiftId = openResult.Data!.Id;

            var movementCount = random.Next(1, 15);
            var runningTotal = 0m;

            for (var i = 0; i < movementCount; i++)
            {
                var type = random.Next(4) switch
                {
                    0 => "sale",
                    1 => "refund",
                    2 => "paid_in",
                    _ => "paid_out"
                };
                var amount = Math.Round((decimal)(random.NextDouble() * 100) + 1, 2);

                var moveResult = await svc.RecordMovementAsync(
                    shiftId, type, amount, null, null, identityUserId, tenantId);

                Assert.True(moveResult.IsSuccess, moveResult.Error);
                runningTotal += moveResult.Data!.Amount;
            }

            var closeResult = await svc.CloseAsync(openingFloat + runningTotal, null, identityUserId, tenantId);

            Assert.True(closeResult.IsSuccess, closeResult.Error);
            Assert.Equal(openingFloat + runningTotal, closeResult.Data!.ExpectedCash);
        }
    }
}
