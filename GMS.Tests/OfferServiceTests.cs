namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Offers;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;

public class OfferServiceTests
{
    private static (GymFlowProDbContext ctx, OfferService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var promo = new PromoService(ctx, new Repository<PromoCode>(ctx), tenantContext, NullLogger<PromoService>.Instance);
        var svc = new OfferService(ctx, promo, NullLogger<OfferService>.Instance);
        return (ctx, svc, tenantId);
    }

    private static DateOnly TodayCairo() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time")));

    private static UpsertOfferRequest VisibleMembershipOffer(string name = "Summer")
    {
        var today = TodayCairo();
        return new UpsertOfferRequest
        {
            Name = name,
            ShortDescription = "Save 20%",
            Start = today.AddDays(-5),
            End = today.AddDays(30),
            AppliesTo = "memberships",
            MembershipLabels = new List<string> { "3 Months" },
            DiscountType = "percentage",
            Value = 20,
            ShowOnMemberApp = true,
            Featured = true,
            DisplayOrder = 1,
            Redemption = "code",
            PromoCode = "SUMMER20",
            AllMembers = true
        };
    }

    [Fact]
    public async Task MemberList_HidesDraftExpiredScheduledAndAppOff()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var today = TodayCairo();

        var created = await svc.CreateAsync(tenantId, VisibleMembershipOffer());
        Assert.True(created.IsSuccess, created.Error);

        await svc.CreateAsync(tenantId, new UpsertOfferRequest
        {
            Name = "Hidden welcome",
            ShortDescription = "off",
            Start = today.AddDays(-10),
            End = today.AddDays(10),
            AppliesTo = "memberships",
            DiscountType = "percentage",
            Value = 15,
            ShowOnMemberApp = false,
            Redemption = "automatic"
        });

        await svc.CreateAsync(tenantId, new UpsertOfferRequest
        {
            Name = "Draft",
            ShortDescription = "draft",
            Start = today,
            End = today.AddDays(10),
            AppliesTo = "memberships",
            DiscountType = "percentage",
            Value = 10,
            ShowOnMemberApp = true,
            IsDraft = true,
            Redemption = "automatic"
        });

        await svc.CreateAsync(tenantId, new UpsertOfferRequest
        {
            Name = "Expired merch",
            ShortDescription = "ended",
            Start = today.AddDays(-40),
            End = today.AddDays(-10),
            AppliesTo = "products",
            DiscountType = "percentage",
            Value = 10,
            ShowOnMemberApp = true,
            Redemption = "automatic"
        });

        await svc.CreateAsync(tenantId, new UpsertOfferRequest
        {
            Name = "Future protein",
            ShortDescription = "later",
            Start = today.AddDays(10),
            End = today.AddDays(20),
            AppliesTo = "products",
            DiscountType = "bxgy",
            BuyQty = 2,
            GetQty = 1,
            ShowOnMemberApp = true,
            Redemption = "automatic"
        });

        var list = await svc.ListMemberAsync(tenantId, Guid.NewGuid());
        Assert.True(list.IsSuccess);
        Assert.Single(list.Data!);
        Assert.Equal("Summer", list.Data![0].Name);
        Assert.Equal("Have a code?", list.Data[0].PromoCodeHint);
        Assert.DoesNotContain("SUMMER20", System.Text.Json.JsonSerializer.Serialize(list.Data[0]));
    }

    [Fact]
    public async Task Create_WithPromoCode_CreatesLinkedPromo()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var result = await svc.CreateAsync(tenantId, VisibleMembershipOffer());
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Data!.PromoCodeId);
        Assert.Equal("SUMMER20", result.Data.PromoCode);
        Assert.Equal("active", result.Data.Status);

        var promo = await ctx.PromoCodes.FirstAsync(p => p.Id == result.Data.PromoCodeId);
        Assert.Equal("SUMMER20", promo.Code);
        Assert.Equal("percent", promo.Type);
        Assert.Equal(20, promo.Value);
    }

    [Fact]
    public async Task End_ExpiresOffer_AndDeactivatesPromo()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var created = await svc.CreateAsync(tenantId, VisibleMembershipOffer());
        var ended = await svc.EndAsync(tenantId, created.Data!.Id);
        Assert.True(ended.IsSuccess);
        Assert.Equal("expired", ended.Data!.Status);

        var promo = await ctx.PromoCodes.FirstAsync(p => p.Id == created.Data.PromoCodeId);
        Assert.False(promo.IsActive);
    }

    [Fact]
    public async Task MemberGet_UnknownId_Fails()
    {
        var (_, svc, tenantId) = CreateSut();
        var result = await svc.GetMemberByIdAsync(tenantId, Guid.NewGuid(), Guid.NewGuid());
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task StaffList_IncludesHiddenAndDraft()
    {
        var (_, svc, tenantId) = CreateSut();
        await svc.CreateAsync(tenantId, VisibleMembershipOffer());
        await svc.CreateAsync(tenantId, new UpsertOfferRequest
        {
            Name = "Desk only",
            ShortDescription = "POS",
            Start = new DateOnly(2026, 1, 1),
            End = new DateOnly(2026, 12, 31),
            AppliesTo = "memberships",
            DiscountType = "percentage",
            Value = 15,
            ShowOnMemberApp = false,
            IsDraft = true,
            Redemption = "automatic"
        });

        var staff = await svc.ListStaffAsync(tenantId);
        Assert.Equal(2, staff.Data!.Count);
    }
}
