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

public class MemberStoreServiceTests
{
    private class NoOpMemberOrderNotifier : IMemberOrderNotifier
    {
        public Task NotifyCreatedAsync(Guid tenantId, Guid orderId, string orderNumber, Guid memberId, string memberName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyStatusChangedAsync(Guid tenantId, Guid orderId, string orderNumber, string status, Guid memberId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class Fixture
    {
        public GymFlowProDbContext Ctx { get; init; } = null!;
        public MemberStoreService Store { get; init; } = null!;
        public StockLedgerService Ledger { get; init; } = null!;
        public Guid TenantId { get; init; }
        public Guid WarehouseId { get; init; }
        public Guid MemberIdentityId { get; init; }
        public Guid MemberId { get; init; }
        public Guid StaffIdentityId { get; init; }
        public Guid VisibleProductId { get; init; }
        public Guid HiddenProductId { get; init; }
    }

    private static async Task<Fixture> CreateAsync()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Store Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Store Gym",
            NameAr = "صالة",
            GymCode = $"S-{tenantId:N}"[..12],
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

        var visible = new Product
        {
            TenantId = tenantId,
            Sku = "SHAKE-1",
            Name = "Protein Shake",
            SellPrice = 80m,
            CostPrice = 40m,
            Currency = "EGP",
            TrackStock = true,
            IsSellable = true,
            VisibleToMembers = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var hidden = new Product
        {
            TenantId = tenantId,
            Sku = "HIDE-1",
            Name = "Staff Only",
            SellPrice = 50m,
            CostPrice = 20m,
            Currency = "EGP",
            TrackStock = true,
            IsSellable = true,
            VisibleToMembers = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Products.AddRange(visible, hidden);

        var memberIdentityId = Guid.NewGuid();
        var staffIdentityId = Guid.NewGuid();

        var memberAppUser = new AppUser
        {
            TenantId = tenantId,
            UserId = memberIdentityId.ToString(),
            Email = "member@test.local",
            FirstName = "Test",
            LastName = "Member",
            Role = "Member",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var staffAppUser = new AppUser
        {
            TenantId = tenantId,
            UserId = staffIdentityId.ToString(),
            Email = "desk@test.local",
            FirstName = "Desk",
            LastName = "Staff",
            Role = "staff",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.AddRange(memberAppUser, staffAppUser);

        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "M-001",
            FullName = "Test Member",
            PhoneNumber = "+201000000099",
            Email = "member@test.local",
            IsActive = true,
            AppUserId = memberAppUser.Id,
            DateOfBirth = new DateOnly(1995, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.GymMembers.Add(member);
        await ctx.SaveChangesAsync();

        // Wire navigation for AppUser filter joins in FindMember.
        member.AppUser = memberAppUser;
        await ctx.SaveChangesAsync();

        var ledger = new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance);
        var open = await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = visible.Id,
            WarehouseId = warehouse.Id,
            QtyDelta = 10,
            Reason = StockMovementReasons.Opening,
            UnitCost = 40
        });
        Assert.True(open.IsSuccess, open.Error);

        var hiddenOpen = await ledger.PostAsync(new StockLedgerPostRequest
        {
            TenantId = tenantId,
            ProductId = hidden.Id,
            WarehouseId = warehouse.Id,
            QtyDelta = 5,
            Reason = StockMovementReasons.Opening,
            UnitCost = 20
        });
        Assert.True(hiddenOpen.IsSuccess, hiddenOpen.Error);

        var audit = new AuditService(
            ctx,
            new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            tenantContext,
            NullLogger<AuditService>.Instance);

        var store = new MemberStoreService(
            ctx,
            ledger,
            audit,
            new NoOpMemberOrderNotifier());

        return new Fixture
        {
            Ctx = ctx,
            Store = store,
            Ledger = ledger,
            TenantId = tenantId,
            WarehouseId = warehouse.Id,
            MemberIdentityId = memberIdentityId,
            MemberId = member.Id,
            StaffIdentityId = staffIdentityId,
            VisibleProductId = visible.Id,
            HiddenProductId = hidden.Id
        };
    }

    [Fact]
    public async Task ListStoreProducts_OnlyVisibleSellable()
    {
        var f = await CreateAsync();
        var result = await f.Store.ListStoreProductsAsync(f.TenantId);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Data!);
        Assert.Equal(f.VisibleProductId, result.Data[0].Id);
        Assert.Equal(10m, result.Data[0].AvailableQty);
        Assert.True(result.Data[0].InStock);
    }

    [Fact]
    public async Task CreateOrder_RejectsHiddenProduct()
    {
        var f = await CreateAsync();
        var result = await f.Store.CreateOrderAsync(f.TenantId, f.MemberIdentityId, new CreateMemberOrderRequest
        {
            Lines = { new CreateMemberOrderLineRequest { ProductId = f.HiddenProductId, Qty = 1 } }
        });
        Assert.False(result.IsSuccess);
        Assert.Contains("not available", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOrder_SnapshotsPrice_AndDoesNotTouchLedger()
    {
        var f = await CreateAsync();
        var movementsBefore = await f.Ctx.StockMovements.CountAsync();

        var result = await f.Store.CreateOrderAsync(f.TenantId, f.MemberIdentityId, new CreateMemberOrderRequest
        {
            Notes = "After class",
            Lines = { new CreateMemberOrderLineRequest { ProductId = f.VisibleProductId, Qty = 2 } }
        });
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(MemberOrderStatuses.Pending, result.Data!.Status);
        Assert.Equal(160m, result.Data.Total);
        Assert.Equal(80m, result.Data.Lines[0].UnitPrice);
        Assert.Equal(2m, result.Data.Lines[0].Qty);

        // Mutate catalog price after order — snapshot must stay.
        var product = await f.Ctx.Products.SingleAsync(p => p.Id == f.VisibleProductId);
        product.SellPrice = 999m;
        await f.Ctx.SaveChangesAsync();

        var again = await f.Store.GetMyOrderAsync(f.TenantId, f.MemberIdentityId, result.Data.Id);
        Assert.Equal(80m, again.Data!.Lines[0].UnitPrice);
        Assert.Equal(160m, again.Data.Total);

        Assert.Equal(movementsBefore, await f.Ctx.StockMovements.CountAsync());
        var avail = await f.Ledger.GetAvailableAsync(f.TenantId, f.VisibleProductId, f.WarehouseId);
        Assert.Equal(10m, avail.Data);
    }

    [Fact]
    public async Task CreateOrder_InsufficientStock_Fails()
    {
        var f = await CreateAsync();
        var result = await f.Store.CreateOrderAsync(f.TenantId, f.MemberIdentityId, new CreateMemberOrderRequest
        {
            Lines = { new CreateMemberOrderLineRequest { ProductId = f.VisibleProductId, Qty = 99 } }
        });
        Assert.False(result.IsSuccess);
        Assert.Contains("Insufficient", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemberCannotSeeOtherMembersOrder()
    {
        var f = await CreateAsync();
        var created = await f.Store.CreateOrderAsync(f.TenantId, f.MemberIdentityId, new CreateMemberOrderRequest
        {
            Lines = { new CreateMemberOrderLineRequest { ProductId = f.VisibleProductId, Qty = 1 } }
        });
        Assert.True(created.IsSuccess, created.Error);

        var otherIdentity = Guid.NewGuid();
        var otherApp = new AppUser
        {
            TenantId = f.TenantId,
            UserId = otherIdentity.ToString(),
            Email = "other@test.local",
            FirstName = "Other",
            LastName = "Member",
            Role = "Member",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        f.Ctx.AppUsers.Add(otherApp);
        var otherMember = new GymMember
        {
            TenantId = f.TenantId,
            MemberNumber = "M-002",
            FullName = "Other Member",
            PhoneNumber = "+201000000088",
            IsActive = true,
            AppUserId = otherApp.Id,
            DateOfBirth = new DateOnly(1990, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        };
        f.Ctx.GymMembers.Add(otherMember);
        await f.Ctx.SaveChangesAsync();

        var peek = await f.Store.GetMyOrderAsync(f.TenantId, otherIdentity, created.Data!.Id);
        Assert.False(peek.IsSuccess);
    }

    [Fact]
    public async Task Lifecycle_Pending_Accept_Ready_Complete()
    {
        var f = await CreateAsync();
        var created = await f.Store.CreateOrderAsync(f.TenantId, f.MemberIdentityId, new CreateMemberOrderRequest
        {
            Lines = { new CreateMemberOrderLineRequest { ProductId = f.VisibleProductId, Qty = 1 } }
        });
        Assert.True(created.IsSuccess, created.Error);
        var id = created.Data!.Id;
        var movementsBefore = await f.Ctx.StockMovements.CountAsync();

        var accepted = await f.Store.AcceptAsync(f.TenantId, id, f.StaffIdentityId);
        Assert.True(accepted.IsSuccess, accepted.Error);
        Assert.Equal(MemberOrderStatuses.Accepted, accepted.Data!.Status);

        var ready = await f.Store.MarkReadyAsync(f.TenantId, id, f.StaffIdentityId);
        Assert.True(ready.IsSuccess, ready.Error);
        Assert.Equal(MemberOrderStatuses.Ready, ready.Data!.Status);

        var completed = await f.Store.CompleteAsync(f.TenantId, id, f.StaffIdentityId);
        Assert.True(completed.IsSuccess, completed.Error);
        Assert.Equal(MemberOrderStatuses.Completed, completed.Data!.Status);

        Assert.Equal(movementsBefore, await f.Ctx.StockMovements.CountAsync());
        var avail = await f.Ledger.GetAvailableAsync(f.TenantId, f.VisibleProductId, f.WarehouseId);
        Assert.Equal(10m, avail.Data);
    }

    [Fact]
    public async Task Lifecycle_Pending_Reject_AndIllegalTransition()
    {
        var f = await CreateAsync();
        var created = await f.Store.CreateOrderAsync(f.TenantId, f.MemberIdentityId, new CreateMemberOrderRequest
        {
            Lines = { new CreateMemberOrderLineRequest { ProductId = f.VisibleProductId, Qty = 1 } }
        });
        var id = created.Data!.Id;

        var bad = await f.Store.MarkReadyAsync(f.TenantId, id, f.StaffIdentityId);
        Assert.False(bad.IsSuccess);

        var rejected = await f.Store.RejectAsync(f.TenantId, id, f.StaffIdentityId, new RejectMemberOrderRequest
        {
            Reason = "Out of stock at counter"
        });
        Assert.True(rejected.IsSuccess, rejected.Error);
        Assert.Equal(MemberOrderStatuses.Rejected, rejected.Data!.Status);
        Assert.Equal("Out of stock at counter", rejected.Data.RejectionReason);

        var acceptAfter = await f.Store.AcceptAsync(f.TenantId, id, f.StaffIdentityId);
        Assert.False(acceptAfter.IsSuccess);
    }

    [Fact]
    public async Task StaffList_FiltersByStatus()
    {
        var f = await CreateAsync();
        await f.Store.CreateOrderAsync(f.TenantId, f.MemberIdentityId, new CreateMemberOrderRequest
        {
            Lines = { new CreateMemberOrderLineRequest { ProductId = f.VisibleProductId, Qty = 1 } }
        });

        var pending = await f.Store.ListOrdersForStaffAsync(f.TenantId, MemberOrderStatuses.Pending);
        Assert.True(pending.IsSuccess);
        Assert.Single(pending.Data!);

        var completed = await f.Store.ListOrdersForStaffAsync(f.TenantId, MemberOrderStatuses.Completed);
        Assert.Empty(completed.Data!);
    }
}
