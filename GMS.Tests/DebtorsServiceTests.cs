namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class DebtorsServiceTests
{
    private class NoOpPaymobService : IPaymobService
    {
        public Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone) =>
            Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string hmacHeader) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private class RecordingWhatsAppService : IWhatsAppService
    {
        public List<(string Phone, string Template)> TemplateSends { get; } = new();

        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr) => Task.CompletedTask;

        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters)
        {
            TemplateSends.Add((phone, templateName));
            return Task.CompletedTask;
        }
    }

    /// <summary>Minimal in-memory IDistributedCache fake supporting real expiry, since the reminder
    /// throttle test needs to simulate a TTL lapsing (Redis isn't available in this test process).</summary>
    private class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        /// <summary>Test-only helper simulating the 48h throttle TTL lapsing.</summary>
        public void ExpireAll() => _store.Clear();
    }

    private static (GymFlowProDbContext ctx, DebtorsService svc, RecordingWhatsAppService whatsApp, FakeDistributedCache cache, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var cache = new FakeDistributedCache();
        var whatsApp = new RecordingWhatsAppService();

        var svc = new DebtorsService(ctx, cache, whatsApp, new NoOpPaymobService(), NullLogger<DebtorsService>.Instance);

        return (ctx, svc, whatsApp, cache, tenantId);
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

    private static Sale SeedPartiallyPaidSale(GymFlowProDbContext ctx, Guid tenantId, Guid memberId, decimal amountDue, DateOnly dueDate, string? description = null)
    {
        var sale = new Sale
        {
            TenantId = tenantId,
            MemberId = memberId,
            SoldByUserId = Guid.NewGuid(),
            Subtotal = amountDue + 100m,
            Total = amountDue + 100m,
            AmountDue = amountDue,
            DueDate = dueDate,
            Status = "partially_paid"
        };
        ctx.Sales.Add(sale);
        ctx.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "membership",
            Description = description ?? "Sale",
            Qty = 1,
            UnitPrice = sale.Total,
            LineTotal = sale.Total
        });
        return sale;
    }

    [Theory]
    [InlineData(3, "0-7")]
    [InlineData(15, "8-30")]
    [InlineData(35, "30+")]
    public void ComputeAgingBucket_DaysSinceDue_ReturnsExpectedBucket(int daysSinceDue, string expectedBucket)
    {
        Assert.Equal(expectedBucket, DebtorsService.ComputeAgingBucket(daysSinceDue));
    }

    [Fact]
    public async Task GetDebtorsPagedAsync_AgingBucket_ComputedFromOldestDueDate()
    {
        var (ctx, svc, _, _, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        SeedPartiallyPaidSale(ctx, tenantId, member.Id, 200m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-15)));
        await ctx.SaveChangesAsync();

        var result = await svc.GetDebtorsPagedAsync(tenantId, page: 1, pageSize: 20);

        Assert.True(result.IsSuccess, result.Error);
        var debtor = Assert.Single(result.Data!.Items);
        Assert.Equal("8-30", debtor.AgingBucket);
        Assert.Equal(200m, debtor.TotalDue);
    }

    [Fact]
    public async Task GetDebtorsPagedAsync_MemberIdFilter_ReturnsOnlyThatBuyer()
    {
        var (ctx, svc, _, _, tenantId) = CreateSut();
        var buyer = SeedMember(ctx, tenantId);
        var other = SeedMember(ctx, tenantId);
        SeedPartiallyPaidSale(ctx, tenantId, buyer.Id, 150m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)));
        SeedPartiallyPaidSale(ctx, tenantId, other.Id, 90m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)));
        await ctx.SaveChangesAsync();

        var result = await svc.GetDebtorsPagedAsync(tenantId, page: 1, pageSize: 20, buyer.Id);

        Assert.True(result.IsSuccess, result.Error);
        var row = Assert.Single(result.Data!.Items);
        Assert.Equal(buyer.Id, row.MemberId);
        Assert.Equal(150m, row.TotalDue);
    }

    [Fact]
    public async Task RemindAsync_SecondCallWithin48h_FailsThrottle_ThenSucceedsAfterTtlExpires()
    {
        var (ctx, svc, whatsApp, cache, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        SeedPartiallyPaidSale(ctx, tenantId, member.Id, 200m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)));
        await ctx.SaveChangesAsync();

        var first = await svc.RemindAsync(member.Id, tenantId, Guid.NewGuid());
        Assert.True(first.IsSuccess, first.Error);
        Assert.Single(whatsApp.TemplateSends);

        var second = await svc.RemindAsync(member.Id, tenantId, Guid.NewGuid());
        Assert.False(second.IsSuccess);
        Assert.StartsWith(DebtorFailureReasons.ReminderThrottle + "|", second.Error);
        Assert.Single(whatsApp.TemplateSends); // no new send while throttled

        cache.ExpireAll(); // simulate the 48h TTL lapsing

        var third = await svc.RemindAsync(member.Id, tenantId, Guid.NewGuid());
        Assert.True(third.IsSuccess, third.Error);
        Assert.Equal(2, whatsApp.TemplateSends.Count);
    }

    [Fact]
    public async Task GetOutstandingSalesAsync_ReturnsOldestDueFirst_WithPaidAndRemaining()
    {
        var (ctx, svc, _, _, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        var other = SeedMember(ctx, tenantId);
        var newer = SeedPartiallyPaidSale(ctx, tenantId, member.Id, 500m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), "Protein");
        var older = SeedPartiallyPaidSale(ctx, tenantId, member.Id, 1000m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), "Gold 12 months");
        SeedPartiallyPaidSale(ctx, tenantId, other.Id, 90m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), "Other");
        ctx.Sales.Add(new Sale
        {
            TenantId = tenantId,
            MemberId = member.Id,
            SoldByUserId = Guid.NewGuid(),
            Subtotal = 200m,
            Total = 200m,
            AmountDue = 0m,
            Status = "completed"
        });
        ctx.Sales.Add(new Sale
        {
            TenantId = tenantId,
            MemberId = member.Id,
            SoldByUserId = Guid.NewGuid(),
            Subtotal = 300m,
            Total = 300m,
            AmountDue = 300m,
            Status = "refunded"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.GetOutstandingSalesAsync(tenantId, member.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1500m, result.Data!.TotalDue);
        Assert.Equal(2, result.Data.Sales.Count);
        Assert.Equal(older.Id, result.Data.Sales[0].SaleId);
        Assert.Equal("Gold 12 months", result.Data.Sales[0].Description);
        Assert.Equal(1000m, result.Data.Sales[0].AmountDue);
        Assert.Equal(100m, result.Data.Sales[0].Paid);
        Assert.Equal(newer.Id, result.Data.Sales[1].SaleId);
        Assert.Equal("Protein", result.Data.Sales[1].Description);
    }

    [Fact]
    public async Task GetOutstandingSalesAsync_NothingDue_ReturnsEmpty200Shape()
    {
        var (ctx, svc, _, _, tenantId) = CreateSut();
        var member = SeedMember(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var result = await svc.GetOutstandingSalesAsync(tenantId, member.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0m, result.Data!.TotalDue);
        Assert.Empty(result.Data.Sales);
        Assert.Equal(member.Id, result.Data.MemberId);
    }
}
