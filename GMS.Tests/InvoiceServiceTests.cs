namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class InvoiceServiceTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;";

    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private static (GymFlowProDbContext ctx, InvoiceService svc, Guid tenantId) CreateSut(bool useLocalDb = false)
    {
        var tenantId = Guid.NewGuid();

        var options = useLocalDb
            ? new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(LocalDbConnectionString).Options
            : new DbContextOptionsBuilder<GymFlowProDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var svc = new InvoiceService(ctx, auditService, NullLogger<InvoiceService>.Instance);

        return (ctx, svc, tenantId);
    }

    private static InvoiceService CreateLocalDbService(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>().UseSqlServer(LocalDbConnectionString).Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        return new InvoiceService(ctx, auditService, NullLogger<InvoiceService>.Instance);
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

    private static GymMember SeedMember(GymFlowProDbContext ctx, Guid tenantId)
    {
        var member = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = $"M-{Guid.NewGuid():N}".Substring(0, 8),
            FullName = "Test Member",
            FullNameAr = "عضو اختبار",
            PhoneNumber = $"+2010{Random.Shared.Next(10000000, 99999999)}",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25))
        };
        ctx.GymMembers.Add(member);
        return member;
    }

    private static Sale SeedSale(
        GymFlowProDbContext ctx, Guid tenantId, Guid memberId, Guid staffId,
        decimal subtotal, decimal discount, decimal tax, decimal total)
    {
        var sale = new Sale
        {
            TenantId = tenantId,
            MemberId = memberId,
            SoldByUserId = staffId,
            Subtotal = subtotal,
            DiscountAmount = discount,
            TaxAmount = tax,
            Total = total,
            Status = "completed"
        };
        ctx.Sales.Add(sale);
        return sale;
    }

    private static async Task CleanupLocalDbAsync(GymFlowProDbContext ctx, Guid tenantId)
    {
        await ctx.Invoices.Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.InvoiceSequences.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Sales.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.GymMembers.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.AppUsers.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync();
        await ctx.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TaxSettingsChange_DoesNotRetroactivelyUpdateExistingInvoiceVatRate()
    {
        var (ctx, svc, tenantId) = CreateSut(useLocalDb: true);
        SeedTenant(ctx, tenantId, settingsJson: "{\"vat_enabled\":true,\"vat_rate\":0.14}");
        var (staff, _) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var sale = SeedSale(ctx, tenantId, member.Id, staff.Id, subtotal: 500m, discount: 0m, tax: 70m, total: 570m);
        await ctx.SaveChangesAsync();

        try
        {
            await svc.CreateForSaleAsync(sale.Id);

            var invoice = await ctx.Invoices.SingleAsync(i => i.SaleId == sale.Id);
            Assert.Equal(0.14m, invoice.VatRate);

            // Simulate a tax settings change made AFTER this invoice was issued.
            var tenant = await ctx.Tenants.SingleAsync(t => t.Id == tenantId);
            tenant.Settings = "{\"vat_enabled\":true,\"vat_rate\":0.20}";
            await ctx.SaveChangesAsync();

            // The already-issued invoice's VatRate is a snapshot of the rate actually applied at
            // issue time — it must not follow the tenant's current configuration.
            var reloadedInvoice = await ctx.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal(0.14m, reloadedInvoice.VatRate);
        }
        finally
        {
            await CleanupLocalDbAsync(ctx, tenantId);
        }
    }

    [Fact]
    public async Task EnqueueForSale_ZeroTotalSale_SkipsEnqueueingWithoutThrowing()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, _) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var sale = SeedSale(ctx, tenantId, member.Id, staff.Id, 0m, 0m, 0m, 0m);
        await ctx.SaveChangesAsync();

        // If EnqueueForSale tried to actually enqueue, this would throw — Hangfire's JobStorage
        // isn't configured in this test process. Not throwing IS the assertion that it skipped.
        var exception = await Record.ExceptionAsync(() => svc.EnqueueForSale(sale.Id));

        Assert.Null(exception);
    }

    [Fact]
    public async Task VoidAsync_MarksInvoiceVoided_RemainsReadable_AndWritesAuditEvent()
    {
        var (ctx, svc, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var (staff, identityUserId) = SeedStaff(ctx, tenantId);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            Type = "invoice",
            InvoiceNumber = "INV-2026-000001",
            MemberNameSnapshot = "Test Member",
            MemberPhoneSnapshot = "+201000000000",
            LinesSnapshot = "[]",
            Subtotal = 100m,
            DiscountAmount = 0m,
            VatRate = 0m,
            VatAmount = 0m,
            Total = 100m,
            IssuedAt = DateTime.UtcNow,
            Status = "issued"
        };
        ctx.Invoices.Add(invoice);
        await ctx.SaveChangesAsync();

        var result = await svc.VoidAsync(invoice.Id, "Refund issued", identityUserId);

        Assert.True(result.IsSuccess, result.Error);

        var reloaded = await ctx.Invoices.SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal("voided", reloaded.Status);
        Assert.Equal("Refund issued", reloaded.VoidReason);
        Assert.Equal(staff.Id, reloaded.VoidedByUserId);

        var auditEvent = await ctx.AuditEvents
            .SingleOrDefaultAsync(a => a.Action == "invoice.void" && a.EntityId == invoice.Id);
        Assert.NotNull(auditEvent);
    }

    /// <summary>
    /// Gap-free numbering uses a raw UPDATE...OUTPUT with UPDLOCK, which EF Core's InMemory
    /// provider cannot execute at all — this needs a real relational engine, same as
    /// PromoServiceTests'/SaleServiceTests' race tests. Seeds/cleans up its own isolated rows.
    /// </summary>
    [Fact]
    public async Task CreateForSaleAsync_FiftyParallelSalesSameTenant_ProducesGapFreeSequentialNumbers()
    {
        var (ctx, _, tenantId) = CreateSut(useLocalDb: true);

        SeedTenant(ctx, tenantId);
        var (staff, _) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);

        var saleIds = new List<Guid>();
        for (var i = 0; i < 50; i++)
            saleIds.Add(SeedSale(ctx, tenantId, member.Id, staff.Id, 100m, 0m, 0m, 100m).Id);

        await ctx.SaveChangesAsync();

        try
        {
            await Parallel.ForEachAsync(saleIds, async (saleId, _) =>
            {
                var svc = CreateLocalDbService(tenantId);
                await svc.CreateForSaleAsync(saleId);
            });

            var numbers = await ctx.Invoices
                .Where(i => i.TenantId == tenantId)
                .Select(i => i.InvoiceNumber)
                .ToListAsync();

            Assert.Equal(50, numbers.Count);
            Assert.Equal(50, numbers.Distinct().Count());

            var year = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone).Year;
            var expected = Enumerable.Range(1, 50).Select(n => $"INV-{year}-{n:D6}").ToHashSet();
            Assert.Equal(expected, numbers.ToHashSet());
        }
        finally
        {
            await CleanupLocalDbAsync(ctx, tenantId);
        }
    }

    [Fact]
    public async Task CreateForSaleAsync_CalledTwiceForSameSale_CreatesExactlyOneInvoice()
    {
        var (ctx, svc, tenantId) = CreateSut(useLocalDb: true);
        SeedTenant(ctx, tenantId);
        var (staff, _) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var sale = SeedSale(ctx, tenantId, member.Id, staff.Id, 100m, 0m, 0m, 100m);
        await ctx.SaveChangesAsync();

        try
        {
            await svc.CreateForSaleAsync(sale.Id);
            await svc.CreateForSaleAsync(sale.Id);

            var count = await ctx.Invoices.CountAsync(i => i.SaleId == sale.Id);
            Assert.Equal(1, count);
        }
        finally
        {
            await CleanupLocalDbAsync(ctx, tenantId);
        }
    }

    [Fact]
    public async Task CreateForSaleAsync_VatEnabled_ComputesCorrectVatAmountAndTotal()
    {
        var (ctx, svc, tenantId) = CreateSut(useLocalDb: true);
        SeedTenant(ctx, tenantId, settingsJson: "{\"vat_enabled\":true,\"vat_rate\":0.14}");
        var (staff, _) = SeedStaff(ctx, tenantId);
        var member = SeedMember(ctx, tenantId);
        var sale = SeedSale(ctx, tenantId, member.Id, staff.Id, subtotal: 500m, discount: 0m, tax: 70m, total: 570m);
        await ctx.SaveChangesAsync();

        try
        {
            await svc.CreateForSaleAsync(sale.Id);

            var invoice = await ctx.Invoices.SingleAsync(i => i.SaleId == sale.Id);
            Assert.Equal(70.00m, invoice.VatAmount);
            Assert.Equal(570.00m, invoice.Total);
            Assert.Equal(0.14m, invoice.VatRate);
        }
        finally
        {
            await CleanupLocalDbAsync(ctx, tenantId);
        }
    }
}
