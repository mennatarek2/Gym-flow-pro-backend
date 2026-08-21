namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Invoices;
using GMS.Application.DTOs.Trials;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Tests.Helpers;

public class TrialServiceTests
{
    private class NoOpOtpSender : IOtpSender
    {
        public Task SendOtpAsync(string phoneNumber, string otp) => Task.CompletedTask;
    }

    /// <summary>Minimal in-memory IDistributedCache fake — TTL is not enforced, only exact-match
    /// storage/retrieval/removal, which is all TrialService's pending-signup flow needs in tests.</summary>
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
    }

    private class NoOpInvoiceService : IInvoiceService
    {
        public Task EnqueueForSale(Guid saleId) => Task.CompletedTask;
        public Task CreateForSaleAsync(Guid saleId) => Task.CompletedTask;
        public Task<Result<Guid>> CreateCreditNoteAsync(Guid refundId) =>
            Task.FromResult(Result<Guid>.Failure("not implemented in test double"));
        public Task<Result<PagedResult<InvoiceDto>>> GetPagedAsync(Guid tenantId, InvoiceQueryRequest query) =>
            Task.FromResult(Result<PagedResult<InvoiceDto>>.Failure("not implemented in test double"));
        public Task<Result<InvoiceDto>> GetByIdAsync(Guid id) =>
            Task.FromResult(Result<InvoiceDto>.Failure("not implemented in test double"));
        public Task<Result<bool>> ResendAsync(Guid invoiceId) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> VoidAsync(Guid invoiceId, string reason, Guid voidedByUserId) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<PaymentReceiptInfoDto>> GetPaymentInfoAsync(Guid paymentTransactionId) =>
            Task.FromResult(Result<PaymentReceiptInfoDto>.Failure("not implemented in test double"));
        public Task<Result<Guid>> GetOriginalInvoiceIdForSaleAsync(Guid saleId) =>
            Task.FromResult(Result<Guid>.Failure("not implemented in test double"));
    }

    private static (GymFlowProDbContext ctx, TrialService svc, IOtpService otpService, Guid tenantId) CreateSut(
        Func<GymFlowProDbContext, IInvoiceService>? invoiceServiceFactory = null)
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        var ctx = new GymFlowProDbContext(options, tenantContext);
        var otpService = new OtpService(new MemoryCache(new MemoryCacheOptions()), new NoOpOtpSender(), NullLogger<OtpService>.Instance);
        var cache = new FakeDistributedCache();
        var invoiceService = invoiceServiceFactory?.Invoke(ctx) ?? new NoOpInvoiceService();

        var svc = new TrialService(
            ctx, new MemberRepository(ctx), otpService, cache,
            invoiceService, new UnlimitedTierEnforcement(), NullLogger<TrialService>.Instance);

        return (ctx, svc, otpService, tenantId);
    }

    private static void SeedTenant(GymFlowProDbContext ctx, Guid tenantId)
    {
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة اختبار",
            GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
            City = "Cairo",
            Address = "Test Address",
            PhoneNumber = "0100000000",
            Email = $"{tenantId}@test.local",
            SubscriptionStartDate = DateTime.UtcNow
        });
    }

    /// <summary>Returns the Identity id (JWT "sub") — NOT AppUser.Id — since that's what
    /// TrialService's staffUserId parameter is compared against (via AppUser.UserId).</summary>
    private static Guid SeedStaff(GymFlowProDbContext ctx, Guid tenantId)
    {
        var identityUserId = Guid.NewGuid();
        var staff = new AppUser
        {
            TenantId = tenantId,
            UserId = identityUserId.ToString(),
            FirstName = "Front",
            LastName = "Desk",
            Email = $"staff-{Guid.NewGuid()}@test.local",
            Role = "Receptionist"
        };
        ctx.AppUsers.Add(staff);
        return identityUserId;
    }

    private static MembershipPlan SeedTrialPlan(GymFlowProDbContext ctx, Guid tenantId)
    {
        var plan = new MembershipPlan
        {
            TenantId = tenantId,
            Name = "Free Trial",
            NameAr = "تجربة مجانية",
            PlanType = "trial",
            DurationDays = 14,
            Price = 0m
        };
        ctx.MembershipPlans.Add(plan);
        return plan;
    }

    [Theory]
    [InlineData("0100-123-4567")]
    [InlineData("+201001234567")]
    [InlineData("20100-1234567")]
    public async Task InitiateAsync_PhoneAlreadyUsedForATrial_RejectedRegardlessOfFormat(string rawPhoneVariant)
    {
        var (ctx, svc, _, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var plan = SeedTrialPlan(ctx, tenantId);
        await ctx.SaveChangesAsync();

        // A prior trial already exists for +201001234567 (the normalized form of all 3 variants).
        var priorMember = new GymMember
        {
            TenantId = tenantId,
            MemberNumber = "GYM-000",
            FullName = "Prior Trial User",
            FullNameAr = "مستخدم تجربة سابق",
            PhoneNumber = "+201001234567",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)),
            IsTrial = false,
            TrialOutcome = "expired"
        };
        ctx.GymMembers.Add(priorMember);

        ctx.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            MemberId = priorMember.Id,
            PlanId = plan.Id,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-16),
            Status = "expired"
        });
        await ctx.SaveChangesAsync();

        var result = await svc.InitiateAsync(new TrialInitiateRequest
        {
            FullName = "New Prospect",
            PhoneNumber = rawPhoneVariant,
            PlanId = plan.Id
        }, tenantId);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("TRIAL_ALREADY_USED|", result.Error);
    }

    [Fact]
    public async Task InitiateAsync_NewPhone_SendsOtpAndAllowsConfirm()
    {
        var (ctx, svc, otpService, tenantId) = CreateSut();
        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        var plan = SeedTrialPlan(ctx, tenantId);
        await ctx.SaveChangesAsync();

        const string phone = "+201112223333";

        var initiateResult = await svc.InitiateAsync(new TrialInitiateRequest
        {
            FullName = "Brand New Prospect",
            FullNameAr = "عميل جديد",
            PhoneNumber = phone,
            PlanId = plan.Id
        }, tenantId);

        Assert.True(initiateResult.IsSuccess, initiateResult.Error);
        Assert.True(initiateResult.Data!.OtpSent);
        Assert.Equal(600, initiateResult.Data.ExpiresInSeconds);

        // OtpService stores one OTP per (phone, tenant) — re-generating overwrites it with a code
        // the test can capture directly (mirrors what the real SMS gateway would receive).
        var otp = await otpService.GenerateOtpAsync(phone, tenantId);

        var confirmResult = await svc.ConfirmAsync(
            new TrialConfirmRequest { PhoneNumber = phone, Otp = otp }, staffId, tenantId);

        Assert.True(confirmResult.IsSuccess, confirmResult.Error);
        Assert.Equal("Brand New Prospect", confirmResult.Data!.Member.FullName);
        Assert.Equal("trial", confirmResult.Data.Membership.PlanType);

        var createdMember = await ctx.GymMembers.SingleAsync(m => m.PhoneNumber == phone);
        Assert.True(createdMember.IsTrial);
        Assert.Equal("active_trial", createdMember.TrialOutcome);
    }

    [Fact]
    public async Task ConfirmAsync_ZeroTotalTrialSale_DoesNotAttemptToEnqueueInvoiceJob()
    {
        // Uses the REAL InvoiceService (not a stub) so this genuinely proves the zero-total
        // short-circuit fires — if it didn't, EnqueueForSale would try BackgroundJob.Enqueue with
        // no Hangfire storage configured in this test process, throwing and failing the test.
        var (ctx, svc, otpService, tenantId) = CreateSut(invoiceServiceFactory: c =>
        {
            var tenantContext = new TenantContext();
            var auditService = new AuditService(c, new Microsoft.AspNetCore.Http.HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
            return new InvoiceService(c, auditService, NullLogger<InvoiceService>.Instance);
        });

        SeedTenant(ctx, tenantId);
        var staffId = SeedStaff(ctx, tenantId);
        var plan = SeedTrialPlan(ctx, tenantId);
        await ctx.SaveChangesAsync();

        const string phone = "+201115556666";

        var initiateResult = await svc.InitiateAsync(new TrialInitiateRequest
        {
            FullName = "Zero Total Prospect",
            PhoneNumber = phone,
            PlanId = plan.Id
        }, tenantId);
        Assert.True(initiateResult.IsSuccess, initiateResult.Error);

        var otp = await otpService.GenerateOtpAsync(phone, tenantId);

        var confirmResult = await svc.ConfirmAsync(
            new TrialConfirmRequest { PhoneNumber = phone, Otp = otp }, staffId, tenantId);

        Assert.True(confirmResult.IsSuccess, confirmResult.Error);

        var sale = await ctx.Sales.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(0m, sale.Total);
        Assert.Equal(0, await ctx.Invoices.CountAsync());
    }
}
