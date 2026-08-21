namespace GMS.Tests;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Promo;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;

public class PromoServiceTests
{
    private static (GymFlowProDbContext ctx, PromoService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var svc = new PromoService(ctx, new Repository<PromoCode>(ctx), tenantContext, NullLogger<PromoService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static MembershipPlan SeedPlan(GymFlowProDbContext ctx, Guid tenantId, decimal price)
    {
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Monthly Unlimited",
            NameAr = "شهري",
            PlanType = "monthly_unlimited",
            DurationDays = 30,
            Price = price
        };
        ctx.MembershipPlans.Add(plan);
        return plan;
    }

    private static PromoCode SeedPromo(GymFlowProDbContext ctx, Guid tenantId, Action<PromoCode>? configure = null)
    {
        var promo = new PromoCode
        {
            TenantId = tenantId,
            Code = "SAVE10",
            Type = "percent",
            Value = 10,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            ValidTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            IsActive = true
        };
        configure?.Invoke(promo);
        ctx.PromoCodes.Add(promo);
        return promo;
    }

    [Fact]
    public async Task ValidateAndPriceAsync_UnknownCode_ReturnsCodeNotFound()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 500);
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("NOPE", plan.Id, Guid.NewGuid(), tenantId);

        Assert.True(result.IsSuccess);
        Assert.False(result.Data!.IsValid);
        Assert.Equal(PromoValidationReasons.CodeNotFound, result.Data.FailureReason);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_InactiveCode_ReturnsCodeInactive()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 500);
        SeedPromo(ctx, tenantId, p => p.IsActive = false);
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, Guid.NewGuid(), tenantId);

        Assert.False(result.Data!.IsValid);
        Assert.Equal(PromoValidationReasons.CodeInactive, result.Data.FailureReason);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_OutsideValidDateRange_ReturnsDateRangeInvalid()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 500);
        SeedPromo(ctx, tenantId, p =>
        {
            p.ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
            p.ValidTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, Guid.NewGuid(), tenantId);

        Assert.False(result.Data!.IsValid);
        Assert.Equal(PromoValidationReasons.DateRangeInvalid, result.Data.FailureReason);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_MaxUsesReached_ReturnsMaxUsesReached()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 500);
        SeedPromo(ctx, tenantId, p =>
        {
            p.MaxUses = 5;
            p.UsesCount = 5;
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, Guid.NewGuid(), tenantId);

        Assert.False(result.Data!.IsValid);
        Assert.Equal(PromoValidationReasons.MaxUsesReached, result.Data.FailureReason);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_MemberAlreadyUsedItMaxTimes_ReturnsMemberMaxUsesReached()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 500);
        var promo = SeedPromo(ctx, tenantId, p => p.MaxUsesPerMember = 1);
        var memberId = Guid.NewGuid();

        ctx.Sales.Add(new Sale
        {
            TenantId = tenantId,
            MemberId = memberId,
            SoldByUserId = Guid.NewGuid(),
            PromoCodeId = promo.Id,
            Status = "completed",
            Subtotal = 500,
            Total = 450
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, memberId, tenantId);

        Assert.False(result.Data!.IsValid);
        Assert.Equal(PromoValidationReasons.MemberMaxUsesReached, result.Data.FailureReason);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_RefundedPriorSale_DoesNotCountTowardMemberMaxUses()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 500);
        var promo = SeedPromo(ctx, tenantId, p => p.MaxUsesPerMember = 1);
        var memberId = Guid.NewGuid();

        ctx.Sales.Add(new Sale
        {
            TenantId = tenantId,
            MemberId = memberId,
            SoldByUserId = Guid.NewGuid(),
            PromoCodeId = promo.Id,
            Status = "refunded",
            Subtotal = 500,
            Total = 450
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, memberId, tenantId);

        Assert.True(result.Data!.IsValid);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_PlanNotInAppliesTo_ReturnsPlanNotInScope()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var scopedPlan = SeedPlan(ctx, tenantId, 500);
        var otherPlan = SeedPlan(ctx, tenantId, 300);
        SeedPromo(ctx, tenantId, p => p.AppliesTo = JsonSerializer.Serialize(new[] { scopedPlan.Id }));
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", otherPlan.Id, Guid.NewGuid(), tenantId);

        Assert.False(result.Data!.IsValid);
        Assert.Equal(PromoValidationReasons.PlanNotInScope, result.Data.FailureReason);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_FinalPriceBelowMinPrice_ReturnsBelowMinPrice_AndDoesNotClamp()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 500);
        SeedPromo(ctx, tenantId, p =>
        {
            p.Type = "fixed";
            p.Value = 490;
            p.MinPrice = 50;
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, Guid.NewGuid(), tenantId);

        Assert.False(result.Data!.IsValid);
        Assert.Equal(PromoValidationReasons.BelowMinPrice, result.Data.FailureReason);
        // Rejected — pricing fields must NOT be populated with a clamped value.
        Assert.Null(result.Data.FinalPrice);
    }

    [Theory]
    [InlineData(500, 72.50, 427.50)]
    [InlineData(100, 14.50, 85.50)]
    public async Task ValidateAndPriceAsync_PercentDiscount_RoundsHalfUpTo2Decimals(
        decimal price, decimal expectedDiscount, decimal expectedFinalPrice)
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, price);
        SeedPromo(ctx, tenantId, p =>
        {
            p.Type = "percent";
            p.Value = 14.5m;
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, Guid.NewGuid(), tenantId);

        Assert.True(result.Data!.IsValid);
        Assert.Equal(expectedDiscount, result.Data.DiscountAmount);
        Assert.Equal(expectedFinalPrice, result.Data.FinalPrice);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_FixedDiscount_NeverExceedsPrice()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var plan = SeedPlan(ctx, tenantId, 50);
        SeedPromo(ctx, tenantId, p =>
        {
            p.Type = "fixed";
            p.Value = 200; // larger than the plan price
        });
        await ctx.SaveChangesAsync();

        var result = await svc.ValidateAndPriceAsync("SAVE10", plan.Id, Guid.NewGuid(), tenantId);

        Assert.True(result.Data!.IsValid);
        Assert.Equal(50m, result.Data.DiscountAmount);
        Assert.Equal(0m, result.Data.FinalPrice);
    }

    /// <summary>
    /// TryConsumeAsync uses a raw conditional UPDATE (ExecuteSqlInterpolatedAsync), which EF Core's
    /// InMemory provider does not support at all. Proving the UPDATE is actually atomic under real
    /// concurrency requires a real relational engine's row locking, so this test runs against the
    /// same LocalDB instance the rest of the app uses (see GMS.Api/appsettings.json DefaultConnection).
    /// It seeds/cleans up its own isolated row via raw SQL so it doesn't depend on or pollute any
    /// other data in the database.
    /// </summary>
    [Fact]
    public async Task TryConsumeAsync_TwentyConcurrentCallers_OnlyFiveSucceedWhenMaxUsesIsFive()
    {
        const string connectionString = "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;";

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantId = Guid.NewGuid();
        var promoId = Guid.NewGuid();

        await using (var seed = new GymFlowProDbContext(options, null))
        {
            await seed.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO tenants (Id, Name, NameAr, GymCode, City, Address, PhoneNumber, Email, LogoUrl, TimeZone, Currency, MaxMembers, IsActive, SubscriptionStartDate, IsDeleted, CreatedAtUtc)
                VALUES ({tenantId}, 'Race Test Tenant', 'مستأجر اختبار', {"RACE-" + tenantId.ToString("N")[..12]}, 'Cairo', 'Test', '0100000000', {"race-" + tenantId + "@test.local"}, '', 'Africa/Cairo', 'EGP', 1000, 1, SYSUTCDATETIME(), 0, SYSUTCDATETIME())");

            await seed.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO promo_codes (Id, TenantId, Code, Type, Value, ValidFrom, ValidTo, MaxUses, UsesCount, IsActive, IsDeleted, CreatedAtUtc)
                VALUES ({promoId}, {tenantId}, 'RACE_TEST', 'percent', 10, '2020-01-01', '2099-01-01', 5, 0, 1, 0, SYSUTCDATETIME())");
        }

        try
        {
            var results = new bool[20];

            await Parallel.ForEachAsync(Enumerable.Range(0, 20), async (i, _) =>
            {
                var tenantContext = new TenantContext();
                tenantContext.SetTenant(tenantId, "Race Tenant", "Africa/Cairo");

                await using var ctx = new GymFlowProDbContext(options, tenantContext);
                var svc = new PromoService(ctx, new Repository<PromoCode>(ctx), tenantContext, NullLogger<PromoService>.Instance);

                results[i] = await svc.TryConsumeAsync(promoId, tenantId);
            });

            Assert.Equal(5, results.Count(r => r));

            await using var verify = new GymFlowProDbContext(options, null);
            var finalUsesCount = await verify.PromoCodes
                .IgnoreQueryFilters()
                .Where(p => p.Id == promoId)
                .Select(p => p.UsesCount)
                .FirstAsync();

            Assert.Equal(5, finalUsesCount);
        }
        finally
        {
            await using var cleanup = new GymFlowProDbContext(options, null);
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM promo_codes WHERE Id = {promoId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE Id = {tenantId}");
        }
    }
}
