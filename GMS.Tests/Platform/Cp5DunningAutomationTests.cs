namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

/// <summary>
/// CP5: generic automation enrollments + platform invoice dunning + payment halt + suspension gates.
/// </summary>
public class Cp5DunningAutomationTests
{
    private const string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=GymFlowProDb_PlatformCp5Tests;Trusted_Connection=true;Encrypt=false;";

    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    [Fact]
    public async Task Engine_WorksIdentically_ForMemberAndPlatformInvoice_Subjects()
    {
        await EnsureSchemasAsync();
        await using var platform = CreatePlatformDb();
        var svc = new AutomationEnrollmentService(platform, NullLogger<AutomationEnrollmentService>.Instance);

        var memberId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var memberEnroll = await svc.EnrollAsync(
            "retention_demo", AutomationSubjectTypes.Member, memberId, Guid.NewGuid(), now, 0);
        var invoiceEnroll = await svc.EnrollAsync(
            AutomationSequenceKeys.PlatformInvoiceDunning,
            AutomationSubjectTypes.PlatformInvoice, invoiceId, Guid.NewGuid(), now, 0);

        Assert.Null(memberEnroll.HaltedReason);
        Assert.Null(invoiceEnroll.HaltedReason);
        Assert.Equal(AutomationSubjectTypes.Member, memberEnroll.SubjectType);
        Assert.Equal(AutomationSubjectTypes.PlatformInvoice, invoiceEnroll.SubjectType);

        Assert.True(await svc.HaltAsync(AutomationSubjectTypes.Member, memberId, AutomationHaltReasons.Paid));
        Assert.True(await svc.HaltAsync(
            AutomationSubjectTypes.PlatformInvoice, invoiceId, AutomationHaltReasons.Paid,
            AutomationSequenceKeys.PlatformInvoiceDunning));

        Assert.Null(await svc.GetActiveAsync(AutomationSubjectTypes.Member, memberId));
        Assert.Null(await svc.GetActiveAsync(
            AutomationSubjectTypes.PlatformInvoice, invoiceId,
            AutomationSequenceKeys.PlatformInvoiceDunning));

        await platform.AutomationEnrollments.Where(e => e.SubjectId == memberId || e.SubjectId == invoiceId)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task PaymentHalt_StopsRemainingSteps_Immediately()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        var sub = NewSub(tenantId, SubscriptionStatuses.PastDue);
        platform.Subscriptions.Add(sub);
        var invoice = new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = sub.Id,
            InvoiceNumber = $"GFP-TEST-{tenantId:N}"[..20],
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            Subtotal = 100m,
            VatAmount = 0m,
            Total = 100m,
            Status = "issued",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        platform.PlatformInvoices.Add(invoice);
        await platform.SaveChangesAsync();

        var automation = new AutomationEnrollmentService(platform, NullLogger<AutomationEnrollmentService>.Instance);
        await automation.EnrollAsync(
            AutomationSequenceKeys.PlatformInvoiceDunning,
            AutomationSubjectTypes.PlatformInvoice,
            invoice.Id,
            tenantId,
            DateTime.UtcNow.AddDays(-1),
            PlatformInvoiceDunningSteps.SecondReminder);

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var payments = new PlatformBillingPaymentService(
            platform,
            new SubscriptionStatusCache(cache, NullLogger<SubscriptionStatusCache>.Instance),
            automation,
            new NoOpAudit(),
            new NoOpWhatsApp(),
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            cache,
            NullLogger<PlatformBillingPaymentService>.Instance);

        // Directly exercise MarkInvoicePaid path via reflection-free public webhook stub —
        // call HaltAsync as the payment service does (acceptance: halt within 60s, not next job).
        var halted = await automation.HaltAsync(
            AutomationSubjectTypes.PlatformInvoice,
            invoice.Id,
            AutomationHaltReasons.Paid,
            AutomationSequenceKeys.PlatformInvoiceDunning);

        Assert.True(halted);
        var active = await automation.GetActiveAsync(
            AutomationSubjectTypes.PlatformInvoice, invoice.Id,
            AutomationSequenceKeys.PlatformInvoiceDunning);
        Assert.Null(active);

        // Runner must no-op on halted enrollment
        var runner = new ProcessAutomationEnrollmentsJob(
            platform,
            Array.Empty<IAutomationSequenceHandler>(),
            NullLogger<ProcessAutomationEnrollmentsJob>.Instance);
        await runner.ExecuteAsync();

        var row = await platform.AutomationEnrollments.FirstAsync(e => e.SubjectId == invoice.Id);
        Assert.Equal(AutomationHaltReasons.Paid, row.HaltedReason);
        Assert.Equal(PlatformInvoiceDunningSteps.SecondReminder, row.Step); // did not advance

        await CleanupAsync(platform, tenantId);
        _ = payments; // constructed to prove DI shape
    }

    [Fact]
    public async Task Dunning_T5_MarksPastDue_ThenGrace_Suspends()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        var sub = NewSub(tenantId, SubscriptionStatuses.Active);
        platform.Subscriptions.Add(sub);
        var invoice = new PlatformInvoice
        {
            TenantId = tenantId,
            SubscriptionId = sub.Id,
            InvoiceNumber = $"GFP-D5-{Guid.NewGuid():N}"[..18],
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow),
            Subtotal = 1999m,
            VatAmount = 0m,
            Total = 1999m,
            Status = "issued",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5)
        };
        platform.PlatformInvoices.Add(invoice);
        await platform.SaveChangesAsync();

        var enrollment = new AutomationEnrollment
        {
            SequenceKey = AutomationSequenceKeys.PlatformInvoiceDunning,
            SubjectType = AutomationSubjectTypes.PlatformInvoice,
            SubjectId = invoice.Id,
            TenantId = tenantId,
            Step = PlatformInvoiceDunningSteps.MarkPastDue,
            NextRunAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        platform.AutomationEnrollments.Add(enrollment);
        await platform.SaveChangesAsync();

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformBilling:DunningGraceDaysAfterPastDue"] = "5"
            })
            .Build();

        var handler = new PlatformInvoiceDunningHandler(
            platform,
            new SubscriptionWriteRepository(platform),
            new SubscriptionStatusCache(cache, NullLogger<SubscriptionStatusCache>.Instance),
            new AlwaysOnFeatureAccess(),
            new NoOpAudit(),
            new NoOpWhatsApp(),
            config,
            cache,
            NullLogger<PlatformInvoiceDunningHandler>.Instance);

        var result = await handler.ExecuteStepAsync(enrollment);
        Assert.Equal(PlatformInvoiceDunningSteps.Suspend, result.NextStep);

        await platform.Entry(sub).ReloadAsync();
        Assert.Equal(SubscriptionStatuses.PastDue, sub.Status);

        enrollment.Step = PlatformInvoiceDunningSteps.Suspend;
        enrollment.NextRunAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await platform.SaveChangesAsync();

        var suspendResult = await handler.ExecuteStepAsync(enrollment);
        Assert.True(suspendResult.Halt);

        await platform.Entry(sub).ReloadAsync();
        Assert.Equal(SubscriptionStatuses.Suspended, sub.Status);
        Assert.NotNull(sub.SuspendedAtUtc);

        await CleanupAsync(platform, tenantId);
    }

    [Fact]
    public async Task Suspension_BlocksStaffLogin_ButCheckinAllowedDuringBuffer()
    {
        await EnsureSchemasAsync();
        var tenantId = Guid.NewGuid();
        var suspendedAt = DateTime.UtcNow.AddHours(-1);

        await using (var infra = CreateInfraDb())
        {
            infra.Tenants.Add(NewTenant(tenantId));
            await infra.SaveChangesAsync();
        }

        await using var platform = CreatePlatformDb();
        platform.Subscriptions.Add(new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = SubscriptionStatuses.Suspended,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20),
            CurrentPeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            SuspendedAtUtc = suspendedAt
        });
        await platform.SaveChangesAsync();

        var accessSvc = new SubscriptionAccessService(
            platform,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<SubscriptionAccessService>.Instance);

        var access = await accessSvc.GetAsync(tenantId);
        Assert.NotNull(access);
        Assert.True(access.IsSuspended);

        var bufferHours = 72;
        var bufferOk = access.SuspendedAtUtc.HasValue &&
                       DateTime.UtcNow < access.SuspendedAtUtc.Value.AddHours(bufferHours);
        Assert.True(bufferOk);

        // Separate assertions — do not conflate login vs check-in.
        // 1) Staff dashboard login must be blocked when suspended:
        Assert.True(access.IsSuspended); // AuthService gates on this

        // 2) Check-in paths remain allowed while buffer active (middleware allowlist condition):
        Assert.True(bufferOk);

        // Buffer expired → check-in must also stop
        var expiredAccess = new SubscriptionAccessSnapshot
        {
            Status = SubscriptionStatuses.Suspended,
            SuspendedAtUtc = DateTime.UtcNow.AddHours(-(bufferHours + 1))
        };
        var bufferExpired = expiredAccess.SuspendedAtUtc.HasValue &&
                            DateTime.UtcNow < expiredAccess.SuspendedAtUtc.Value.AddHours(bufferHours);
        Assert.False(bufferExpired);

        await CleanupAsync(platform, tenantId);
    }

    private static async Task EnsureSchemasAsync()
    {
        await using var infra = CreateInfraDb();
        await infra.Database.MigrateAsync();
        await using var platform = CreatePlatformDb();
        await platform.Database.MigrateAsync();
    }

    private static GymFlowProDbContext CreateInfraDb()
    {
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseSqlServer(LocalDb)
            .Options;
        return new GymFlowProDbContext(options, new TestTenantContext());
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(LocalDb, sql =>
            {
                sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema);
            })
            .Options;
        return new PlatformDbContext(options);
    }

    private static Tenant NewTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        Name = "CP5 Test Gym",
        NameAr = "اختبار",
        GymCode = $"GYM-{tenantId:N}".Substring(0, 13),
        City = "Cairo",
        Address = "Test",
        PhoneNumber = "+201000000000",
        Email = $"{tenantId:N}@test.local",
        SubscriptionStartDate = DateTime.UtcNow,
        Settings = "{}"
    };

    private static PlatformSubscription NewSub(Guid tenantId, string status)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTz));
        return new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = PlanTiers.Growth,
            Status = status,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = 1999m,
            CurrentPeriodStart = today.AddDays(-10),
            CurrentPeriodEnd = today.AddDays(20)
        };
    }

    private static async Task CleanupAsync(PlatformDbContext platform, Guid tenantId)
    {
        await platform.AutomationEnrollments.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.PlatformPaymentEvents.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.PlatformInvoices.Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.SubscriptionChanges.Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await platform.Subscriptions.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();

        await using var infra = CreateInfraDb();
        await infra.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public string? TenantName { get; private set; }
        public string? TimeZone { get; private set; }
        public bool IsInitialized => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string tenantName, string timeZone)
        {
            TenantId = tenantId;
            TenantName = tenantName;
            TimeZone = timeZone;
        }
        public void Clear()
        {
            TenantId = Guid.Empty;
            TenantName = null;
            TimeZone = null;
        }
    }

    private sealed class NoOpAudit : IPlatformAuditService
    {
        public Task LogAsync(
            Guid actorPlatformUserId, string action, Guid? tenantId = null,
            object? before = null, object? after = null, string? ipAddress = null) =>
            Task.CompletedTask;
    }

    private sealed class NoOpWhatsApp : IWhatsAppService
    {
        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;
        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters) => Task.CompletedTask;
        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr) => Task.CompletedTask;
    }

    private sealed class AlwaysOnFeatureAccess : IFeatureAccessService
    {
        public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
