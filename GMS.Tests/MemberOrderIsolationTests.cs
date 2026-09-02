namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.DTOs.MemberStore;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

/// <summary>
/// Regression: Member App orders must never leak across members or tenants.
/// </summary>
public class MemberOrderIsolationTests
{
    private sealed class NoOpNotifier : IMemberOrderNotifier
    {
        public Task NotifyCreatedAsync(Guid tenantId, Guid orderId, string orderNumber, Guid memberId, string memberName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyStatusChangedAsync(Guid tenantId, Guid orderId, string orderNumber, string status, Guid memberId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class TwinMembers
    {
        public GymFlowProDbContext Ctx { get; init; } = null!;
        public MemberStoreService Store { get; init; } = null!;
        public Guid TenantId { get; init; }
        public Guid WarehouseId { get; init; }
        public Guid ProductId { get; init; }
        public Guid IdentityA { get; init; }
        public Guid IdentityB { get; init; }
        public Guid MemberAId { get; init; }
        public Guid MemberBId { get; init; }
    }

    private static async Task<TwinMembers> SeedTwinMembersAsync()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Isolation Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Isolation Gym",
            NameAr = "صالة",
            GymCode = $"I-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        var warehouse = new Warehouse
        {
            TenantId = tenantId,
            Code = "MAIN",
            Name = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Warehouses.Add(warehouse);

        var product = new Product
        {
            TenantId = tenantId,
            Sku = "ISO-1",
            Name = "Iso Shake",
            SellPrice = 50m,
            CostPrice = 20m,
            Currency = "EGP",
            TrackStock = true,
            IsSellable = true,
            VisibleToMembers = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Products.Add(product);

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
            MemberNumber = "M-A",
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
            MemberNumber = "M-B",
            FullName = "Member B",
            PhoneNumber = "+201000000002",
            IsActive = true,
            AppUserId = appB.Id,
            DateOfBirth = new DateOnly(1991, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.AddRange(memberA, memberB);
        await ctx.SaveChangesAsync();

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        var open = await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QtyDelta = 100,
            Reason = StockMovementReasons.Opening,
            UnitCost = 20
        });
        Assert.True(open.IsSuccess, open.Error);

        var audit = new AuditService(
            ctx,
            new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            tenantContext,
            NullLogger<AuditService>.Instance);

        var store = new MemberStoreService(ctx, ledger, audit, new NoOpNotifier());

        return new TwinMembers
        {
            Ctx = ctx,
            Store = store,
            TenantId = tenantId,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            IdentityA = identityA,
            IdentityB = identityB,
            MemberAId = memberA.Id,
            MemberBId = memberB.Id
        };
    }

    private static CreateMemberOrderRequest OneLine(Guid productId) => new()
    {
        Lines = { new CreateMemberOrderLineRequest { ProductId = productId, Qty = 1 } }
    };

    [Fact]
    public async Task MemberA_List_Contains_Only_Own_Orders()
    {
        var f = await SeedTwinMembersAsync();
        var a1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        var a2 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        var b1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));
        Assert.True(a1.IsSuccess && a2.IsSuccess && b1.IsSuccess, a1.Error ?? a2.Error ?? b1.Error);

        var list = await f.Store.ListMyOrdersAsync(f.TenantId, f.IdentityA);
        Assert.True(list.IsSuccess, list.Error);
        Assert.Equal(2, list.Data!.Count);
        Assert.All(list.Data, o => Assert.Equal(f.MemberAId, o.MemberId));
        Assert.Contains(list.Data, o => o.Id == a1.Data!.Id);
        Assert.Contains(list.Data, o => o.Id == a2.Data!.Id);
        Assert.DoesNotContain(list.Data, o => o.Id == b1.Data!.Id);
    }

    [Fact]
    public async Task MemberB_List_Contains_Only_Own_Orders()
    {
        var f = await SeedTwinMembersAsync();
        var a1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        var b1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));
        var b2 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));
        Assert.True(a1.IsSuccess && b1.IsSuccess && b2.IsSuccess);

        var list = await f.Store.ListMyOrdersAsync(f.TenantId, f.IdentityB);
        Assert.True(list.IsSuccess, list.Error);
        Assert.Equal(2, list.Data!.Count);
        Assert.All(list.Data, o => Assert.Equal(f.MemberBId, o.MemberId));
        Assert.DoesNotContain(list.Data, o => o.Id == a1.Data!.Id);
    }

    [Fact]
    public async Task MemberA_Cannot_Get_MemberB_Order_By_Id_IDOR()
    {
        var f = await SeedTwinMembersAsync();
        var a1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        var b1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));
        Assert.True(a1.IsSuccess && b1.IsSuccess);

        var peek = await f.Store.GetMyOrderAsync(f.TenantId, f.IdentityA, b1.Data!.Id);
        Assert.False(peek.IsSuccess);
        Assert.Contains("not found", peek.Error!, StringComparison.OrdinalIgnoreCase);

        var own = await f.Store.GetMyOrderAsync(f.TenantId, f.IdentityA, a1.Data!.Id);
        Assert.True(own.IsSuccess);
        Assert.Equal(a1.Data.Id, own.Data!.Id);
    }

    [Fact]
    public async Task Unknown_Identity_Cannot_List_Any_Orders()
    {
        var f = await SeedTwinMembersAsync();
        await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));

        var list = await f.Store.ListMyOrdersAsync(f.TenantId, Guid.NewGuid());
        Assert.False(list.IsSuccess);
        Assert.Contains("not linked", list.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Member_With_No_Orders_Gets_Empty_List_Not_All_Orders()
    {
        var f = await SeedTwinMembersAsync();
        var b1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));
        Assert.True(b1.IsSuccess);

        var list = await f.Store.ListMyOrdersAsync(f.TenantId, f.IdentityA);
        Assert.True(list.IsSuccess, list.Error);
        Assert.Empty(list.Data!);
    }

    [Fact]
    public async Task Cross_Tenant_Orders_Are_Not_Visible()
    {
        var f = await SeedTwinMembersAsync();
        var a1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        Assert.True(a1.IsSuccess);

        var tenantB = Guid.NewGuid();
        var optionsB = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContextB = new TenantContext();
        tenantContextB.SetTenant(tenantB, "Other Gym", "Africa/Cairo");
        var ctxB = new GymFlowProDbContext(optionsB, tenantContextB);

        ctxB.Tenants.Add(new Tenant
        {
            Id = tenantB,
            Name = "Other Gym",
            GymCode = $"O-{tenantB:N}"[..12],
            City = "Giza",
            Address = "y",
            PhoneNumber = "01000000099",
            Email = $"{tenantB:N}@other.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        var whB = new Warehouse
        {
            TenantId = tenantB, Code = "MAIN", Name = "Main", IsDefault = true, IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var prodB = new Product
        {
            TenantId = tenantB, Sku = "TB-1", Name = "Other", SellPrice = 10m, CostPrice = 5m,
            Currency = "EGP", TrackStock = false, IsSellable = true, VisibleToMembers = true,
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
        var identityOther = Guid.NewGuid();
        var appOther = new AppUser
        {
            TenantId = tenantB, UserId = identityOther.ToString(), Email = "other@gym.local",
            FirstName = "Other", LastName = "Gym", Role = "Member", IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var memberOther = new GymMember
        {
            TenantId = tenantB, MemberNumber = "TB-1", FullName = "Tenant B Member",
            PhoneNumber = "+201099999999", IsActive = true, AppUserId = appOther.Id,
            DateOfBirth = new DateOnly(1992, 1, 1), CreatedAtUtc = DateTime.UtcNow
        };
        ctxB.Warehouses.Add(whB);
        ctxB.Products.Add(prodB);
        ctxB.AppUsers.Add(appOther);
        ctxB.GymMembers.Add(memberOther);
        await ctxB.SaveChangesAsync();

        var ledgerB = new StockLedgerService(ctxB, NullLogger<StockLedgerService>.Instance);
        var auditB = new AuditService(
            ctxB, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tenantContextB,
            NullLogger<AuditService>.Instance);
        var storeB = new MemberStoreService(ctxB, ledgerB, auditB, new NoOpNotifier());

        var otherOrder = await storeB.CreateOrderAsync(tenantB, identityOther, OneLine(prodB.Id));
        Assert.True(otherOrder.IsSuccess, otherOrder.Error);

        // Authenticated as Tenant A / Member A — must not see Tenant B orders even if ids collide in memory.
        var listA = await f.Store.ListMyOrdersAsync(f.TenantId, f.IdentityA);
        Assert.True(listA.IsSuccess);
        Assert.Single(listA.Data!);
        Assert.Equal(a1.Data!.Id, listA.Data[0].Id);
        Assert.DoesNotContain(listA.Data, o => o.Id == otherOrder.Data!.Id);

        var crossGet = await f.Store.GetMyOrderAsync(f.TenantId, f.IdentityA, otherOrder.Data!.Id);
        Assert.False(crossGet.IsSuccess);
    }

    [Fact]
    public async Task Staff_List_Still_Sees_All_Tenant_Members_Orders()
    {
        var f = await SeedTwinMembersAsync();
        await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));

        var staff = await f.Store.ListOrdersForStaffAsync(f.TenantId);
        Assert.True(staff.IsSuccess);
        Assert.Equal(2, staff.Data!.Count);
    }

    [Fact]
    public async Task Staff_List_With_MemberId_Returns_Only_That_Members_Orders()
    {
        var f = await SeedTwinMembersAsync();
        var a1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        var a2 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        var b1 = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId));
        Assert.True(a1.IsSuccess && a2.IsSuccess && b1.IsSuccess);

        var forA = await f.Store.ListOrdersForStaffAsync(f.TenantId, status: null, memberId: f.MemberAId);
        Assert.True(forA.IsSuccess, forA.Error);
        Assert.Equal(2, forA.Data!.Count);
        Assert.All(forA.Data, o => Assert.Equal(f.MemberAId, o.MemberId));
        Assert.DoesNotContain(forA.Data, o => o.Id == b1.Data!.Id);

        var forB = await f.Store.ListOrdersForStaffAsync(f.TenantId, status: null, memberId: f.MemberBId);
        Assert.True(forB.IsSuccess, forB.Error);
        Assert.Single(forB.Data!);
        Assert.Equal(b1.Data!.Id, forB.Data[0].Id);
    }

    [Fact]
    public async Task Limit_Is_Applied_After_Member_Filter()
    {
        var f = await SeedTwinMembersAsync();
        // Seed more than enough for B so that if filter ran after Take, A might incorrectly get B's rows.
        for (var i = 0; i < 5; i++)
            Assert.True((await f.Store.CreateOrderAsync(f.TenantId, f.IdentityB, OneLine(f.ProductId))).IsSuccess);

        var aOnly = await f.Store.CreateOrderAsync(f.TenantId, f.IdentityA, OneLine(f.ProductId));
        Assert.True(aOnly.IsSuccess);

        var list = await f.Store.ListMyOrdersAsync(f.TenantId, f.IdentityA);
        Assert.True(list.IsSuccess);
        Assert.Single(list.Data!);
        Assert.Equal(aOnly.Data!.Id, list.Data[0].Id);
    }
}
