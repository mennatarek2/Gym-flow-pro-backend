namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Sales;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public sealed class SaleAdjustmentServiceTests
{
    [Fact]
    public async Task ReconcileBalanceAsync_RepairsOnlyDenormalizedAmountDue_AndAuditsChange()
    {
        var tenantId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test", "Africa/Cairo");
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GymFlowProDbContext(options, tenantContext);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test",
            GymCode = "RECON-TEST",
            City = "Cairo",
            SubscriptionStartDate = DateTime.UtcNow
        });
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityId.ToString(),
            FirstName = "Test",
            LastName = "Owner",
            Email = $"{identityId}@test.local",
            Role = "Owner"
        };
        var sale = new Sale
        {
            TenantId = tenantId,
            SoldByUserId = staff.Id,
            Total = 500m,
            AmountDue = 0m,
            Status = "completed"
        };
        db.AppUsers.Add(staff);
        db.Sales.Add(sale);
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            Gateway = "cash",
            Method = "cash",
            ExternalRef = "reconcile-test",
            Amount = 200m,
            Status = "success",
            SettlementStatus = "settled",
            PaidAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var audit = new AuditService(
            db, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var service = new SaleAdjustmentService(db, audit);

        var result = await service.ReconcileBalanceAsync(tenantId, identityId, sale.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("reconciled", result.Data!.Status);
        Assert.Equal(300m, result.Data.CanonicalAmountDue);
        Assert.Equal(300m, (await db.Sales.SingleAsync()).AmountDue);
        Assert.Contains(await db.AuditEvents.ToListAsync(),
            item => item.Action == "sale.balance.reconciled");
    }

    [Fact]
    public async Task CreateAsync_WriteOffIsCappedAndVisibleInHistory()
    {
        var tenantId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test", "Africa/Cairo");
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GymFlowProDbContext(options, tenantContext);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test",
            GymCode = "ADJ-TEST",
            City = "Cairo",
            SubscriptionStartDate = DateTime.UtcNow
        });
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityId.ToString(),
            FirstName = "Test",
            LastName = "Owner",
            Email = $"{identityId}@test.local",
            Role = "Owner"
        };
        db.AppUsers.Add(staff);
        var sale = new Sale
        {
            TenantId = tenantId,
            SoldByUserId = staff.Id,
            Total = 500m,
            AmountDue = 500m,
            Status = "partially_paid"
        };
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var audit = new AuditService(
            db,
            new HttpContextAccessor(),
            tenantContext,
            NullLogger<AuditService>.Instance);
        var service = new SaleAdjustmentService(db, audit);

        var result = await service.CreateAsync(tenantId, identityId, new CreateSaleAdjustmentRequest
        {
            SaleId = sale.Id,
            Amount = 500m,
            Type = "write_off",
            Reason = "Approved customer write-off"
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("written_off", (await db.Sales.SingleAsync()).Status);
        Assert.Equal(0m, (await db.Sales.SingleAsync()).AmountDue);
        Assert.Single(await db.SaleAdjustments.ToListAsync());
        var history = await service.ListAsync(tenantId, sale.Id);
        Assert.True(history.IsSuccess, history.Error);
        Assert.Single(history.Data!);
    }
}
