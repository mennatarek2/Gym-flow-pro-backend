namespace GMS.Tests;

using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Common;
using GMS.Application.DTOs.Invoices;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Tests.Helpers;

/// <summary>
/// Cross-cutting idempotency sweep across the four money-path re-execution scenarios called out in
/// the launch hardening pass. Two of the four already have dedicated coverage elsewhere and are
/// NOT duplicated here (duplicating passing assertions adds no real coverage) — see:
///   1. POST /api/sales replay with the same X-Idempotency-Key →
///      SaleServiceTests.CreateSaleAsync_IdempotentReplay_ReturnsSameSaleIdWithIsReplayTrue
///   3. CreateForSaleAsync (invoice job) run twice for the same sale →
///      InvoiceServiceTests.CreateForSaleAsync_CalledTwiceForSameSale_CreatesExactlyOneInvoice
/// This file adds the other two, which had no existing coverage:
///   2. RefundService.ApproveAsync run twice for the same refund
///   4. ImportService.ExecuteAsync run twice for the same batch (simulated chunk-processing resume)
/// </summary>
public class IdempotencyReplayTests
{
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

    private class NoOpWhatsAppService : IWhatsAppService
    {
        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr) => Task.CompletedTask;
        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters) => Task.CompletedTask;
    }

    private class NoOpPaymobService : IPaymobService
    {
        public Task<string> CreatePaymentIntentAsync(Guid membershipId, decimal amount, string memberPhone) => Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string hmacHeader) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private class NoOpFawryService : IFawryService
    {
        public Task<string> CreateOrderAsync(Guid membershipId, decimal amount) => Task.FromResult(string.Empty);
        public bool VerifyWebhookSignature(byte[] body, string signature) => true;
        public Task<bool> RefundAsync(string externalRef, decimal amount) => Task.FromResult(false);
    }

    private class NoOpFileStorageService : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{Guid.NewGuid():N}-{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
    }

    static IdempotencyReplayTests()
    {
        Hangfire.JobStorage.Current = new Hangfire.InMemory.InMemoryStorage();
    }

    [Fact]
    public async Task RefundApproveAsync_CalledTwiceForSameRefund_SecondCallRejectedNoDuplicateCredit()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var shiftService = new ShiftService(ctx, auditService, NullLogger<ShiftService>.Instance);
        var refundService = new RefundService(
            ctx, shiftService, new NoOpInvoiceService(), new NoOpWhatsAppService(),
            new NoOpPaymobService(), new NoOpFawryService(), auditService,
            new NoOpReferralRewardService(),
            new StockLedgerService(ctx, NullLogger<StockLedgerService>.Instance),
            NullLogger<RefundService>.Instance);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Test Gym", NameAr = "صالة اختبار",
            GymCode = $"GYM-{tenantId:N}".Substring(0, 13), City = "Cairo", Address = "Test",
            PhoneNumber = "0100000000", Email = $"{tenantId}@test.local", SubscriptionStartDate = DateTime.UtcNow
        });

        var requesterIdentityId = Guid.NewGuid();
        var requester = new AppUser
        {
            TenantId = tenantId, UserId = requesterIdentityId.ToString(),
            FirstName = "Front", LastName = "Desk", Email = $"staff-{requesterIdentityId}@test.local", Role = "Receptionist"
        };
        ctx.AppUsers.Add(requester);

        var approverIdentityId = Guid.NewGuid();
        var approver = new AppUser
        {
            TenantId = tenantId, UserId = approverIdentityId.ToString(),
            FirstName = "Manager", LastName = "One", Email = $"staff-{approverIdentityId}@test.local", Role = "Manager"
        };
        ctx.AppUsers.Add(approver);

        var member = new GymMember
        {
            TenantId = tenantId, MemberNumber = "GYM-001", FullName = "Test Member", FullNameAr = "عضو اختبار",
            PhoneNumber = "+201000000099", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20))
        };
        ctx.GymMembers.Add(member);

        var sale = new Sale
        {
            TenantId = tenantId, MemberId = member.Id, SoldByUserId = requester.Id,
            Subtotal = 500m, Total = 500m, Status = "completed"
        };
        ctx.Sales.Add(sale);
        await ctx.SaveChangesAsync();

        var requestResult = await refundService.RequestAsync(sale.Id, 100m, "credit", "Test refund", requesterIdentityId, tenantId);
        Assert.True(requestResult.IsSuccess, requestResult.Error);

        var firstApprove = await refundService.ApproveAsync(requestResult.Data!.Id, approverIdentityId, tenantId);
        Assert.True(firstApprove.IsSuccess, firstApprove.Error);
        Assert.Equal("executed", firstApprove.Data!.Status);

        var secondApprove = await refundService.ApproveAsync(requestResult.Data.Id, approverIdentityId, tenantId);
        Assert.False(secondApprove.IsSuccess);
        Assert.StartsWith(RefundFailureReasons.NotAwaitingApproval + "|", secondApprove.Error);

        var creditEntries = await ctx.MemberCredits.CountAsync(c => c.MemberId == member.Id);
        Assert.Equal(1, creditEntries);

        var totalCredit = await ctx.MemberCredits.Where(c => c.MemberId == member.Id).SumAsync(c => c.Amount);
        Assert.Equal(100m, totalCredit);
    }

    [Fact]
    public async Task ImportExecuteAsync_CalledTwiceSimulatingResume_SecondCallSkipsAlreadyImportedRows()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        var memberService = new MemberService(
            ctx, new MemberRepository(ctx), new AesEncryptionService(new ConfigurationBuilder().Build()),
            new UnlimitedTierEnforcement(),
            new NoOpReferralAttribution(),
            new NoOpMemberAppActivation(),
            new ActivityEntitlementService(ctx),
            NullLogger<MemberService>.Instance);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);
        var importService = new ImportService(ctx, memberService, new NoOpFileStorageService(), auditService, tenantContext, new AlwaysEnabledFeatureAccess(), NullLogger<ImportService>.Instance);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Test Gym", NameAr = "صالة اختبار",
            GymCode = $"GYM-{tenantId:N}".Substring(0, 13), City = "Cairo", Address = "Test",
            PhoneNumber = "0100000000", Email = $"{tenantId}@test.local", IsActive = true, SubscriptionStartDate = DateTime.UtcNow
        });

        var identityUserId = Guid.NewGuid();
        ctx.AppUsers.Add(new AppUser
        {
            TenantId = tenantId, UserId = identityUserId.ToString(),
            FirstName = "Front", LastName = "Desk", Email = $"staff-{identityUserId}@test.local", Role = "Receptionist"
        });

        ctx.MembershipPlans.Add(new MembershipPlan
        {
            TenantId = tenantId, Name = "Monthly Unlimited", NameAr = "شهري بدون حدود",
            PlanType = "monthly_unlimited", DurationDays = 30, Price = 300m, IsActive = true
        });
        await ctx.SaveChangesAsync();

        var start = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var end = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

        var csv = new StringBuilder();
        csv.AppendLine("Name,Phone,Plan,Start Date,End Date");
        for (var i = 0; i < 10; i++)
            csv.AppendLine($"Member {i},010{i:D8},Monthly Unlimited,{start},{end}");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        var uploadResult = await importService.UploadAsync(stream, "members.csv", "text/csv", identityUserId, tenantId);
        Assert.True(uploadResult.IsSuccess, uploadResult.Error);

        await importService.ValidateAsync(uploadResult.Data!.Id, tenantId);
        await importService.ExecuteAsync(uploadResult.Data.Id, tenantId);

        var memberCountAfterFirstExecute = await ctx.GymMembers.CountAsync(m => m.TenantId == tenantId);
        Assert.Equal(10, memberCountAfterFirstExecute);

        // Simulate a resumed/re-triggered execution of the same batch (e.g. a retried Hangfire job).
        await importService.ExecuteAsync(uploadResult.Data.Id, tenantId);

        var memberCountAfterSecondExecute = await ctx.GymMembers.CountAsync(m => m.TenantId == tenantId);
        Assert.Equal(10, memberCountAfterSecondExecute); // unchanged — no duplicate members created

        var importedRowCount = await ctx.ImportRows.CountAsync(r => r.BatchId == uploadResult.Data.Id && r.Status == "imported");
        Assert.Equal(10, importedRowCount);
    }
}
