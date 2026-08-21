namespace GMS.Tests.Platform;

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Models;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public class PlatformBillingCoreTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb_PlatformCp2Tests;Trusted_Connection=true;Encrypt=false;";

    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    [Fact]
    public async Task EnsureRenewalInvoiceAsync_FiftyParallelSubscriptions_ProducesGapFreeSequentialNumbers()
    {
        await EnsureSchemasAsync();

        var year = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone).Year;
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();

        var tenantIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        foreach (var tenantId in tenantIds)
            infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        var subs = tenantIds.Select(NewActiveSubscription).ToList();
        platform.Subscriptions.AddRange(subs);
        await platform.SaveChangesAsync();

        try
        {
            await Parallel.ForEachAsync(subs, async (sub, ct) =>
            {
                await using var ctx = CreatePlatformDb();
                var svc = CreateInvoiceService(ctx);
                await svc.EnsureRenewalInvoiceAsync(
                    sub,
                    sub.CurrentPeriodEnd.AddDays(1),
                    sub.CurrentPeriodEnd.AddMonths(1),
                    ct);
            });

            var numbers = await platform.PlatformInvoices
                .Where(i => tenantIds.Contains(i.TenantId))
                .Select(i => i.InvoiceNumber)
                .ToListAsync();

            Assert.Equal(50, numbers.Count);
            Assert.Equal(50, numbers.Distinct().Count());

            var expected = Enumerable.Range(1, 50)
                .Select(n => $"GFP-{year}-{n:D6}")
                .ToHashSet();
            Assert.Equal(expected, numbers.ToHashSet());
        }
        finally
        {
            await CleanupAsync(platform, infra, tenantIds);
        }
    }

    [Fact]
    public async Task RenewalJob_RerunAfterCrash_BuildsFinalStateWithoutDuplicatesOrSkips()
    {
        await EnsureSchemasAsync();

        var tenantIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();

        foreach (var tenantId in tenantIds)
            infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        var today = TodayCairo();
        var subs = tenantIds.Select(tid => new PlatformSubscription
        {
            TenantId = tid,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-29),
            CurrentPeriodEnd = today
        }).ToList();

        platform.Subscriptions.AddRange(subs);
        await platform.SaveChangesAsync();

        try
        {
            // First run crashes after processing the first tenant iteration.
            await using (var crashCtx = CreatePlatformDb())
            {
                var job = CreateRenewalJob(crashCtx, new CrashOnSecondPaymentAttemptService());
                await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());
            }

            // Second run resumes cleanly.
            await using (var resumeCtx = CreatePlatformDb())
            {
                var job = CreateRenewalJob(resumeCtx, new AlwaysPaidPaymentService());
                await job.ExecuteAsync();
            }

            await using var verifyCtx = CreatePlatformDb();
            var invoices = await verifyCtx.PlatformInvoices
                .Where(i => tenantIds.Contains(i.TenantId))
                .OrderBy(i => i.TenantId)
                .ToListAsync();
            Assert.Equal(3, invoices.Count);
            Assert.Equal(3, invoices.Select(i => (i.SubscriptionId, i.PeriodStart)).Distinct().Count());

            var advanced = await verifyCtx.Subscriptions
                .Where(s => tenantIds.Contains(s.TenantId))
                .OrderBy(s => s.TenantId)
                .ToListAsync();
            Assert.All(advanced, s =>
            {
                Assert.Equal(today.AddDays(1), s.CurrentPeriodStart);
                Assert.Equal(today.AddDays(1).AddMonths(1).AddDays(-1), s.CurrentPeriodEnd);
            });
        }
        finally
        {
            await CleanupAsync(platform, infra, tenantIds);
        }
    }

    [Fact]
    public async Task CancelAtPeriodEnd_SubscriptionCancelsWithoutGeneratingAnotherRenewalInvoice()
    {
        await EnsureSchemasAsync();

        var tenantId = Guid.NewGuid();
        var today = TodayCairo();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();

        infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        var subscription = new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Pro,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 3999m,
            CurrentPeriodStart = today.AddDays(-29),
            CurrentPeriodEnd = today,
            CancelAtPeriodEnd = true
        };
        platform.Subscriptions.Add(subscription);
        await platform.SaveChangesAsync();

        var existingFinalInvoice = new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = "GFP-HISTORICAL",
            PeriodStart = subscription.CurrentPeriodStart,
            PeriodEnd = subscription.CurrentPeriodEnd,
            Subtotal = subscription.PriceEgp,
            VatAmount = 0m,
            Total = subscription.PriceEgp,
            Currency = "EGP",
            Status = "paid",
            DueDate = subscription.CurrentPeriodStart
        };
        platform.PlatformInvoices.Add(existingFinalInvoice);
        await platform.SaveChangesAsync();

        try
        {
            var job = CreateRenewalJob(platform, new AlwaysPaidPaymentService());
            await job.ExecuteAsync();
            await job.ExecuteAsync(); // no-op rerun

            var invoices = await platform.PlatformInvoices
                .Where(i => i.TenantId == tenantId)
                .ToListAsync();
            Assert.Single(invoices);

            var reloaded = await platform.Subscriptions.SingleAsync(s => s.Id == subscription.Id);
            Assert.Equal(SubscriptionStatuses.Cancelled, reloaded.Status);
            Assert.False(reloaded.CancelAtPeriodEnd);
            Assert.NotNull(reloaded.CancelledAtUtc);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task TryCollectInvoiceAsync_WithSavedCardAndExplicitOptIn_MarksInvoicePaid()
    {
        await EnsureSchemasAsync();

        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        var today = TodayCairo();
        var subscription = new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-29),
            CurrentPeriodEnd = today,
            SavedCardToken = "tok_saved_card",
            AutoRenewOptIn = true
        };
        platform.Subscriptions.Add(subscription);

        var invoice = new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = "GFP-CP3-OPTIN",
            PeriodStart = today.AddDays(1),
            PeriodEnd = today.AddMonths(1),
            Subtotal = 1999m,
            VatAmount = 0m,
            Total = 1999m,
            Status = "issued",
            DueDate = today,
            PdfUrl = "/uploads/platform/optin.pdf"
        };
        platform.PlatformInvoices.Add(invoice);
        await platform.SaveChangesAsync();

        try
        {
            var whatsApp = new RecordingWhatsAppService();
            var service = CreatePlatformPaymentService(platform, whatsApp);

            var result = await service.TryCollectInvoiceAsync(invoice);

            Assert.True(result.Success);
            var reloaded = await platform.PlatformInvoices.SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal("paid", reloaded.Status);
            Assert.Equal("paymob_card", reloaded.PaymentMethod);
            Assert.False(string.IsNullOrWhiteSpace(reloaded.PaymentReference));
            Assert.Single(await platform.PlatformPaymentEvents.Where(e => e.InvoiceId == invoice.Id).ToListAsync());
            Assert.Empty(whatsApp.Documents);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task TryCollectInvoiceAsync_WithoutExplicitOptIn_DoesNotSilentlyCharge()
    {
        await EnsureSchemasAsync();

        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        var today = TodayCairo();
        var subscription = new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-29),
            CurrentPeriodEnd = today,
            SavedCardToken = "tok_saved_card",
            AutoRenewOptIn = false
        };
        platform.Subscriptions.Add(subscription);

        var invoice = new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = "GFP-CP3-NOOPTIN",
            PeriodStart = today.AddDays(1),
            PeriodEnd = today.AddMonths(1),
            Subtotal = 1999m,
            VatAmount = 0m,
            Total = 1999m,
            Status = "issued",
            DueDate = today,
            PdfUrl = "/uploads/platform/nooptin.pdf"
        };
        platform.PlatformInvoices.Add(invoice);
        await platform.SaveChangesAsync();

        try
        {
            var whatsApp = new RecordingWhatsAppService();
            var service = CreatePlatformPaymentService(platform, whatsApp);

            var result = await service.TryCollectInvoiceAsync(invoice);

            Assert.False(result.Success);
            Assert.Equal("MANUAL_PAYMENT_REQUIRED", result.FailureCode);
            var reloaded = await platform.PlatformInvoices.SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal("issued", reloaded.Status);
            Assert.False(string.IsNullOrWhiteSpace(reloaded.PaymentReference));
            Assert.False(string.IsNullOrWhiteSpace(reloaded.PaymentLink));
            Assert.Empty(await platform.PlatformPaymentEvents.Where(e => e.InvoiceId == invoice.Id).ToListAsync());
            Assert.Single(whatsApp.Documents);
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    [Fact]
    public async Task HandleFawryWebhookAsync_ReplayOnlyProcessesOnce_AndReactivatesSuspendedSubscription()
    {
        await EnsureSchemasAsync();

        var tenantId = Guid.NewGuid();
        await using var infra = CreateInfraDb();
        await using var platform = CreatePlatformDb();
        infra.Tenants.Add(NewTenant(tenantId));
        await infra.SaveChangesAsync();

        var today = TodayCairo();
        var subscription = new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Suspended,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-29),
            CurrentPeriodEnd = today
        };
        platform.Subscriptions.Add(subscription);

        var invoice = new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = "GFP-CP3-WEBHOOK",
            PeriodStart = today.AddDays(1),
            PeriodEnd = today.AddMonths(1),
            Subtotal = 1999m,
            VatAmount = 0m,
            Total = 1999m,
            Status = "issued",
            DueDate = today,
            PaymentReference = $"PINV-{Guid.NewGuid():N}",
            PdfUrl = "/uploads/platform/webhook.pdf"
        };
        invoice.PaymentReference = $"PINV-{invoice.Id:N}";
        platform.PlatformInvoices.Add(invoice);
        await platform.SaveChangesAsync();

        try
        {
            var service = CreatePlatformPaymentService(platform, new RecordingWhatsAppService());
            var payload = $$"""
            {"orderStatus":"PAID","merchantRefNum":"{{invoice.PaymentReference}}","fawryRefNumber":"FW-12345","paymentAmount":1999.00}
            """;

            var first = await service.HandleFawryWebhookAsync(payload, "fawry:replay-key");
            var second = await service.HandleFawryWebhookAsync(payload, "fawry:replay-key");

            Assert.True(first.Success);
            Assert.True(second.Duplicate);

            var reloadedInvoice = await platform.PlatformInvoices.SingleAsync(i => i.Id == invoice.Id);
            var reloadedSubscription = await platform.Subscriptions.SingleAsync(s => s.Id == subscription.Id);
            Assert.Equal("paid", reloadedInvoice.Status);
            Assert.Equal("fawry", reloadedInvoice.PaymentMethod);
            Assert.Equal(SubscriptionStatuses.Active, reloadedSubscription.Status);
            Assert.Single(await platform.PlatformPaymentEvents.Where(e => e.InvoiceId == invoice.Id).ToListAsync());
        }
        finally
        {
            await CleanupAsync(platform, infra, new[] { tenantId });
        }
    }

    private static PlatformInvoiceService CreateInvoiceService(PlatformDbContext ctx)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformBilling:DueDays"] = "7",
                ["PlatformBilling:VatRate"] = "0",
                ["PlatformBilling:LegalName"] = "GymFlow",
                ["PlatformBilling:LegalNameAr"] = "جيم فلو",
                ["PlatformBilling:Code"] = "GYMFLOW"
            })
            .Build();

        return new PlatformInvoiceService(
            ctx,
            new CountingPdfRenderer(),
            new NoOpFileStorageService(),
            new NoopAutomationEnrollment(),
            config,
            NullLogger<PlatformInvoiceService>.Instance);
    }

    private static ProcessSubscriptionRenewalsJob CreateRenewalJob(
        PlatformDbContext ctx,
        IPlatformBillingPaymentService payments)
    {
        var repo = new SubscriptionWriteRepository(ctx);
        var cache = new SubscriptionStatusCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<SubscriptionStatusCache>.Instance);

        return new ProcessSubscriptionRenewalsJob(
            ctx,
            repo,
            cache,
            new NoOpAudit(),
            CreateInvoiceService(ctx),
            payments,
            NullLogger<ProcessSubscriptionRenewalsJob>.Instance);
    }

    private static PlatformBillingPaymentService CreatePlatformPaymentService(
        PlatformDbContext ctx,
        RecordingWhatsAppService whatsApp)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformBilling:InstapayBaseUrl"] = "https://instapay.test/pay"
            })
            .Build();
        var httpClient = new HttpClient(new OkHandler()) { BaseAddress = new Uri("https://example.test/") };

        return new PlatformBillingPaymentService(
            ctx,
            new SubscriptionStatusCache(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                NullLogger<SubscriptionStatusCache>.Instance),
            new NoopAutomationEnrollment(),
            new NoOpAudit(),
            whatsApp,
            new PlatformMerchantPaymobService(httpClient, config, NullLogger<PlatformMerchantPaymobService>.Instance),
            new PlatformMerchantFawryService(httpClient, config, NullLogger<PlatformMerchantFawryService>.Instance),
            config,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<PlatformBillingPaymentService>.Instance);
    }

    private static GymFlowProDbContext CreateInfraDb()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid(), "Platform Test", "Africa/Cairo");
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(LocalDbConnectionString)
            .Options;
        return new GymFlowProDbContext(options, tenantContext);
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDbConnectionString, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;
        return new PlatformDbContext(options);
    }

    private static async Task EnsureSchemasAsync()
    {
        await using var infra = CreateInfraDb();
        await infra.Database.MigrateAsync();

        await using var platform = CreatePlatformDb();
        await platform.Database.MigrateAsync();
    }

    private static Tenant NewTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        Name = "Platform Billing Test Gym",
        NameAr = "اختبار فواتير المنصة",
        GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
        City = "Cairo",
        Address = "Test Address",
        PhoneNumber = "+201000000000",
        Email = $"{tenantId:N}@test.local",
        SubscriptionStartDate = DateTime.UtcNow
    };

    private static PlatformSubscription NewActiveSubscription(Guid tenantId)
    {
        var today = TodayCairo();
        return new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-29),
            CurrentPeriodEnd = today
        };
    }

    private static DateOnly TodayCairo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

    private static async Task CleanupAsync(
        PlatformDbContext platform,
        GymFlowProDbContext infra,
        IEnumerable<Guid> tenantIds)
    {
        var ids = tenantIds.ToArray();
        await platform.PlatformInvoices.Where(i => ids.Contains(i.TenantId)).ExecuteDeleteAsync();
        await platform.PlatformPaymentEvents.Where(i => ids.Contains(i.TenantId)).ExecuteDeleteAsync();
        await platform.SubscriptionChanges.Where(c => ids.Contains(c.TenantId)).ExecuteDeleteAsync();
        await platform.Subscriptions.Where(s => ids.Contains(s.TenantId)).ExecuteDeleteAsync();
        await platform.PlatformInvoiceSequences.ExecuteDeleteAsync();
        await platform.PlatformAuditLogs.Where(a => ids.Contains(a.TenantId ?? Guid.Empty)).ExecuteDeleteAsync();

        await infra.Tenants.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync();
    }

    private sealed class CountingPdfRenderer : IInvoicePdfRenderer
    {
        public byte[] Render(InvoicePdfModel model) => new byte[] { 1, 2, 3, 4 };
    }

    private sealed class NoOpFileStorageService : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");

        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
    }

    private sealed class NoOpAudit : IPlatformAuditService
    {
        public Task LogAsync(
            Guid actorPlatformUserId,
            string action,
            Guid? tenantId = null,
            object? before = null,
            object? after = null,
            string? ipAddress = null) => Task.CompletedTask;
    }

    private sealed class AlwaysPaidPaymentService : IPlatformBillingPaymentService
    {
        public Task<bool> HasPaymentMethodOnFileAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<PlatformPaymentAttemptResult> TryCollectInvoiceAsync(
            PlatformInvoice invoice,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformPaymentAttemptResult
            {
                Success = true,
                PaymentMethod = "stub_card",
                PaidAtUtc = DateTime.UtcNow
            });

        public Task<PlatformWebhookProcessResult> HandlePaymobWebhookAsync(
            string rawPayload,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true, Message = "unused" });

        public Task<PlatformWebhookProcessResult> HandleFawryWebhookAsync(
            string rawPayload,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true, Message = "unused" });
    }

    private sealed class CrashOnSecondPaymentAttemptService : IPlatformBillingPaymentService
    {
        private int _attempts;

        public Task<bool> HasPaymentMethodOnFileAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<PlatformPaymentAttemptResult> TryCollectInvoiceAsync(
            PlatformInvoice invoice,
            CancellationToken cancellationToken = default)
        {
            _attempts++;
            if (_attempts == 2)
                throw new InvalidOperationException("Simulated mid-job crash between tenant iterations.");

            return Task.FromResult(new PlatformPaymentAttemptResult
            {
                Success = true,
                PaymentMethod = "stub_card",
                PaidAtUtc = DateTime.UtcNow
            });
        }

        public Task<PlatformWebhookProcessResult> HandlePaymobWebhookAsync(
            string rawPayload,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true, Message = "unused" });

        public Task<PlatformWebhookProcessResult> HandleFawryWebhookAsync(
            string rawPayload,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformWebhookProcessResult { Success = true, Message = "unused" });
    }

    private sealed class RecordingWhatsAppService : IWhatsAppService
    {
        public List<(string Phone, string Url, string Caption)> Documents { get; } = new();

        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters) => Task.CompletedTask;

        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr)
        {
            Documents.Add((phone, documentUrl, caption));
            return Task.CompletedTask;
        }
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"token":"test-token"}""")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class NoopAutomationEnrollment : IAutomationEnrollmentService
    {
        public Task<AutomationEnrollment> EnrollAsync(
            string sequenceKey, string subjectType, Guid subjectId, Guid? tenantId,
            DateTime firstRunAtUtc, int initialStep = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AutomationEnrollment
            {
                SequenceKey = sequenceKey,
                SubjectType = subjectType,
                SubjectId = subjectId,
                TenantId = tenantId,
                Step = initialStep,
                NextRunAtUtc = firstRunAtUtc
            });

        public Task<bool> HaltAsync(
            string subjectType, Guid subjectId, string reason, string? sequenceKey = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<AutomationEnrollment?> GetActiveAsync(
            string subjectType, Guid subjectId, string? sequenceKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AutomationEnrollment?>(null);
    }
}
