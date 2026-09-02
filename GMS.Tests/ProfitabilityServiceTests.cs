namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Constants;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

public sealed class ProfitabilityServiceTests
{
    [Fact]
    public async Task SettlementConfirmation_RequiresVerifiedExternalEvidence()
    {
        var tenantId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TestTenantContext(tenantId);
        await using var db = new GymFlowProDbContext(options, tenantContext);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test",
            GymCode = $"G-{Guid.NewGuid():N}"[..8],
            TimeZone = "Africa/Cairo"
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = paymentId,
            TenantId = tenantId,
            Gateway = "paymob",
            ExternalRef = "gateway-ref",
            Amount = 50m,
            Status = "success",
            SettlementStatus = "pending",
            PaidAtUtc = Utc(10)
        });
        await db.SaveChangesAsync();
        var service = new PaymentService(
            db,
            tenantContext,
            new NoOpReferralAttribution(),
            NullLogger<PaymentService>.Instance);

        var rejected = await service.ConfirmSettlementAsync(
            paymentId, tenantId, "paymob", "gateway-ref", null, false);
        var confirmed = await service.ConfirmSettlementAsync(
            paymentId, tenantId, "paymob", "gateway-ref", "{\"settled\":true}", true);

        Assert.False(rejected.IsSuccess);
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.Equal("settled", (await db.PaymentTransactions.SingleAsync()).SettlementStatus);
    }

    [Fact]
    public async Task CogsBackfill_UsesMatchingHistoricalSaleMovement_AndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new GymFlowProDbContext(options, new TestTenantContext(tenantId));
        var saleId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.SaleLines.Add(new SaleLine
        {
            Id = lineId,
            TenantId = tenantId,
            SaleId = saleId,
            LineType = "retail",
            ReferenceId = productId,
            Qty = 2,
            UnitPrice = 100m,
            LineTotal = 200m
        });
        db.StockMovements.Add(new StockMovement
        {
            TenantId = tenantId,
            ProductId = productId,
            WarehouseId = Guid.NewGuid(),
            QtyDelta = -2m,
            UnitCost = 40m,
            Reason = StockMovementReasons.Sale,
            ReferenceType = StockReferenceTypes.SaleLine,
            ReferenceId = lineId,
            OccurredAtUtc = Utc(10)
        });
        await db.SaveChangesAsync();

        var service = new ProfitabilityService(db);
        var first = await service.BackfillCogsAsync(tenantId);
        var second = await service.BackfillCogsAsync(tenantId);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(1, first.Data!.Backfilled);
        Assert.Equal("RECONSTRUCTABLE", Assert.Single(first.Data.Items).Status);
        Assert.Equal(80m, (await db.SaleLines.SingleAsync()).CogsAmount);
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(0, second.Data!.Backfilled);
        Assert.Equal("ALREADY_RELIABLE", Assert.Single(second.Data.Items).Status);
    }

    [Fact]
    public async Task UnknownSettlementAndMissingPayrollAreNeverReportedAsSettledOrComplete()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TestTenantContext(tenantId);
        await using var db = new GymFlowProDbContext(options, tenantContext);

        db.Sales.Add(new Sale
        {
            TenantId = tenantId,
            Total = 100m,
            AmountDue = 0m,
            Status = "completed",
            CreatedAtUtc = Utc(10)
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            Amount = 100m,
            Status = "success",
            Method = "cash",
            Gateway = "cash",
            ExternalRef = "test-unknown-settlement",
            SettlementStatus = "unknown",
            PaidAtUtc = Utc(10),
            CreatedAtUtc = Utc(10)
        });
        db.SupplierLedgerEntries.Add(new SupplierLedgerEntry
        {
            TenantId = tenantId,
            Amount = -50m,
            Reason = SupplierLedgerReasons.Payment,
            CreatedAtUtc = Utc(11),
            EffectiveAtUtc = Utc(11)
        });
        await db.SaveChangesAsync();

        var result = await new ProfitabilityService(db).GetAsync(
            tenantId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(100m, result.Data!.Collections);
        Assert.Equal(0m, result.Data.SettledCashInflow);
        Assert.False(result.Data.PayrollAvailable);
        Assert.Equal("NO_PAYROLL_PERIOD", result.Data.PayrollCoverageStatus);
        Assert.False(result.Data.SupplierCashPaymentsAvailable);
        Assert.Contains("settlement_data_incomplete", result.Data.DataIssues);
        Assert.Contains("no_payroll_period", result.Data.DataIssues);
        Assert.Contains("supplier_cash_evidence_unavailable", result.Data.DataIssues);
        Assert.Equal(0m, result.Data.SupplierCashPayments);
    }

    private static DateTime Utc(int day) =>
        new(2026, 8, day, 12, 0, 0, DateTimeKind.Utc);

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; }
        public string? TenantName => "Test";
        public string? TimeZone => "Africa/Cairo";
        public bool IsInitialized => true;

        public TestTenantContext(Guid tenantId) => TenantId = tenantId;
        public void SetTenant(Guid tenantId, string tenantName, string timeZone) { }
        public void Clear() { }
    }
}
