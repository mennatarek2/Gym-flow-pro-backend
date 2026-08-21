namespace GMS.Platform.Services;

using System.Data;
using System.Data.Common;
using System.Text.Json;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Core.Models;
using GMS.Core.Utilities;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class PlatformInvoiceService : IPlatformInvoiceService, IPlatformProrationInvoiceService
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly PlatformDbContext _db;
    private readonly IInvoicePdfRenderer _pdfRenderer;
    private readonly IFileStorageService _fileStorage;
    private readonly IAutomationEnrollmentService _automation;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformInvoiceService> _logger;

    public PlatformInvoiceService(
        PlatformDbContext db,
        IInvoicePdfRenderer pdfRenderer,
        IFileStorageService fileStorage,
        IAutomationEnrollmentService automation,
        IConfiguration configuration,
        ILogger<PlatformInvoiceService> logger)
    {
        _db = db;
        _pdfRenderer = pdfRenderer;
        _fileStorage = fileStorage;
        _automation = automation;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PlatformInvoice> EnsureRenewalInvoiceAsync(
        PlatformSubscription subscription,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.PlatformInvoices
            .FirstOrDefaultAsync(
                i => i.SubscriptionId == subscription.Id && i.PeriodStart == periodStart,
                cancellationToken);

        if (existing != null)
            return await EnsurePdfAsync(existing, cancellationToken);

        var lines = new List<InvoicePdfLineModel>
        {
            new()
            {
                Description = $"GymFlow subscription renewal ({subscription.PlanTier})",
                DescriptionAr = $"تجديد اشتراك GymFlow ({subscription.PlanTier})",
                Qty = 1,
                UnitPrice = subscription.PriceEgp,
                LineTotal = subscription.PriceEgp
            }
        };

        // CP6: apply the best active (non-expired) price_override — expires cleanly via ExpiresAtUtc filter.
        var discountLine = await TryBuildCouponDiscountLineAsync(
            subscription.TenantId,
            subscription.PriceEgp,
            cancellationToken);
        if (discountLine != null)
            lines.Add(discountLine);

        // Prior Cairo-month WhatsApp overage from usage_counters (soft overage billed at renewal).
        var priorDay = periodStart.AddDays(-1);
        var priorPeriod = $"{priorDay.Year:D4}-{priorDay.Month:D2}";
        var waOverage = await _db.UsageCounters
            .AsNoTracking()
            .Where(c =>
                c.TenantId == subscription.TenantId &&
                c.Period == priorPeriod &&
                c.Metric == UsageMetrics.WhatsAppMessages &&
                c.OverageBilledEgp != null &&
                c.OverageBilledEgp > 0)
            .Select(c => c.OverageBilledEgp!.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (waOverage > 0)
        {
            lines.Add(new InvoicePdfLineModel
            {
                Description = $"WhatsApp message overage ({priorPeriod})",
                DescriptionAr = $"تجاوز رسائل واتساب ({priorPeriod})",
                Qty = 1,
                UnitPrice = waOverage,
                LineTotal = waOverage
            });
        }

        var subtotal = lines.Sum(l => l.LineTotal);
        var created = await CreateInvoiceAsync(
            subscription,
            periodStart,
            periodEnd,
            subtotal: subtotal,
            paymentMethod: null,
            dueDate: periodStart.AddDays(_configuration.GetValue("PlatformBilling:DueDays", 7)),
            lines: lines,
            cancellationToken: cancellationToken);

        // CP5: enroll platform invoice dunning (idempotent). First step at DueDate 00:00 Cairo.
        await _automation.EnrollAsync(
            AutomationSequenceKeys.PlatformInvoiceDunning,
            AutomationSubjectTypes.PlatformInvoice,
            created.Id,
            created.TenantId,
            PlatformInvoiceDunningHandler.AtDuePlusDaysUtc(created.DueDate, 0),
            PlatformInvoiceDunningSteps.DueReminder,
            cancellationToken);

        return await EnsurePdfAsync(created, cancellationToken);
    }

    public async Task<PlatformInvoice> CreateUpgradeProrationStubAsync(
        Guid tenantId,
        Guid subscriptionId,
        decimal proratedAmountEgp,
        string fromTier,
        string toTier,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken)
            ?? throw new InvalidOperationException($"Subscription {subscriptionId} not found.");

        var today = MembershipOperational.TodayCairo();
        var existing = await _db.PlatformInvoices
            .FirstOrDefaultAsync(
                i => i.SubscriptionId == subscriptionId &&
                     i.PeriodStart == today &&
                     i.PeriodEnd == subscription.CurrentPeriodEnd,
                cancellationToken);

        if (existing != null)
            return await EnsurePdfAsync(existing, cancellationToken);

        var lines = new List<InvoicePdfLineModel>
        {
            new()
            {
                Description = $"GymFlow prorated upgrade {fromTier} -> {toTier}",
                DescriptionAr = $"ترقية نسبية من {fromTier} إلى {toTier}",
                Qty = 1,
                UnitPrice = proratedAmountEgp,
                LineTotal = proratedAmountEgp
            }
        };

        var created = await CreateInvoiceAsync(
            subscription,
            today,
            subscription.CurrentPeriodEnd,
            subtotal: proratedAmountEgp,
            paymentMethod: null,
            dueDate: today,
            lines: lines,
            cancellationToken: cancellationToken);

        return await EnsurePdfAsync(created, cancellationToken);
    }

    private async Task<PlatformInvoice> CreateInvoiceAsync(
        PlatformSubscription subscription,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal subtotal,
        string? paymentMethod,
        DateOnly dueDate,
        IReadOnlyList<InvoicePdfLineModel> lines,
        CancellationToken cancellationToken)
    {
        var year = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone).Year;
        var vatRate = _configuration.GetValue("PlatformBilling:VatRate", 0m);
        var vatAmount = Math.Round(subtotal * vatRate, 2, MidpointRounding.AwayFromZero);
        var total = subtotal + vatAmount;
        var linesSnapshot = JsonSerializer.Serialize(lines);

        var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            var nextNumber = await AllocateNextNumberAsync(year, cancellationToken);
            var invoice = new PlatformInvoice
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                InvoiceNumber = $"GFP-{year}-{nextNumber:D6}",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Subtotal = subtotal,
                VatAmount = vatAmount,
                Total = total,
                Currency = "EGP",
                Status = "issued",
                DueDate = dueDate,
                PaymentMethod = paymentMethod,
                LinesSnapshot = linesSnapshot,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.PlatformInvoices.Add(invoice);
            await _db.SaveChangesAsync(cancellationToken);

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);

            return invoice;
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<int> AllocateNextNumberAsync(int year, CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var results = await _db.Database
                .SqlQuery<int>(
                    $"UPDATE platform.platform_invoice_sequences WITH (UPDLOCK) SET LastNumber = LastNumber + 1 OUTPUT INSERTED.LastNumber WHERE Year = {year}")
                .ToListAsync(cancellationToken);

            if (results.Count > 0)
                return results[0];

            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO platform.platform_invoice_sequences (Id, Year, LastNumber) VALUES ({Guid.NewGuid()}, {year}, 1)",
                    cancellationToken);
                return 1;
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // First-row race — loop back to the UPDLOCK branch.
            }
        }

        throw new InvalidOperationException(
            $"Could not allocate a platform invoice number for year {year} after {maxAttempts} attempts.");
    }

    /// <summary>
    /// Picks the most recently created non-expired price_override and returns a negative invoice line.
    /// Expired rows are ignored automatically — no manual cleanup.
    /// </summary>
    private async Task<InvoicePdfLineModel?> TryBuildCouponDiscountLineAsync(
        Guid tenantId,
        decimal listPriceEgp,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var coupon = await _db.PriceOverrides
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.ExpiresAtUtc > now)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (coupon == null)
            return null;

        decimal discount;
        if (string.Equals(coupon.DiscountType, "percent", StringComparison.OrdinalIgnoreCase))
            discount = Math.Round(listPriceEgp * (coupon.Value / 100m), 2, MidpointRounding.AwayFromZero);
        else
            discount = Math.Round(coupon.Value, 2, MidpointRounding.AwayFromZero);

        discount = Math.Min(discount, listPriceEgp);
        if (discount <= 0)
            return null;

        return new InvoicePdfLineModel
        {
            Description = $"Coupon discount ({coupon.DiscountType} {coupon.Value})",
            DescriptionAr = $"خصم كوبون ({coupon.DiscountType} {coupon.Value})",
            Qty = 1,
            UnitPrice = -discount,
            LineTotal = -discount
        };
    }

    private async Task<PlatformInvoice> EnsurePdfAsync(PlatformInvoice invoice, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(invoice.PdfUrl))
            return invoice;

        var billTo = await LoadTenantBillingInfoAsync(invoice.TenantId, cancellationToken);
        var model = BuildPdfModel(invoice, billTo);
        var pdfBytes = _pdfRenderer.Render(model);

        await using var stream = new MemoryStream(pdfBytes);
        var folder = $"platform-invoices/{invoice.CreatedAtUtc:yyyy}";
        var url = await _fileStorage.UploadAsync(stream, $"{invoice.InvoiceNumber}.pdf", folder);

        invoice.PdfUrl = url;
        await _db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    private InvoicePdfModel BuildPdfModel(PlatformInvoice invoice, TenantBillingInfo billTo)
    {
        var lines = ParseLinesSnapshot(invoice.LinesSnapshot);
        if (lines.Count == 0)
        {
            var fallbackDescription = invoice.PeriodStart == invoice.PeriodEnd
                ? "Proration / رسوم نسبية"
                : $"Subscription period {invoice.PeriodStart:yyyy-MM-dd} → {invoice.PeriodEnd:yyyy-MM-dd}";
            lines.Add(new InvoicePdfLineModel
            {
                Description = fallbackDescription,
                DescriptionAr = billTo.GymCode,
                Qty = 1,
                UnitPrice = invoice.Subtotal,
                LineTotal = invoice.Subtotal
            });
        }

        return new InvoicePdfModel
        {
            InvoiceNumber = invoice.InvoiceNumber,
            Type = "invoice",
            IssuedAt = invoice.CreatedAtUtc,
            TenantName = _configuration["PlatformBilling:LegalName"] ?? "GymFlow",
            TenantNameAr = _configuration["PlatformBilling:LegalNameAr"] ?? "جيم فلو",
            GymCode = _configuration["PlatformBilling:Code"] ?? "GYMFLOW",
            TaxRegistrationNumber = _configuration["PlatformBilling:TaxRegistrationNumber"],
            MemberName = billTo.Name,
            MemberPhone = billTo.PhoneNumber,
            Lines = lines,
            Subtotal = invoice.Subtotal,
            DiscountAmount = 0m,
            VatRate = invoice.Subtotal == 0m ? 0m : invoice.VatAmount / invoice.Subtotal,
            VatAmount = invoice.VatAmount,
            Total = invoice.Total,
            Currency = invoice.Currency,
            BillerCodeLabel = "GymFlow Code",
            CustomerLabel = "Customer",
            CustomerLabelAr = "العميل",
            FooterText = _configuration["PlatformBilling:FooterText"],
            FooterTextAr = _configuration["PlatformBilling:FooterTextAr"]
        };
    }

    private static List<InvoicePdfLineModel> ParseLinesSnapshot(string? linesSnapshot)
    {
        if (string.IsNullOrWhiteSpace(linesSnapshot))
            return new List<InvoicePdfLineModel>();

        try
        {
            return JsonSerializer.Deserialize<List<InvoicePdfLineModel>>(linesSnapshot)
                   ?? new List<InvoicePdfLineModel>();
        }
        catch (JsonException)
        {
            return new List<InvoicePdfLineModel>();
        }
    }

    private async Task<TenantBillingInfo> LoadTenantBillingInfoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return new TenantBillingInfo(tenantId.ToString(), string.Empty, string.Empty);

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) Name, GymCode, PhoneNumber
            FROM dbo.tenants
            WHERE Id = @tenantId
            """;

        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId;
        command.Parameters.Add(param);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new TenantBillingInfo(
                reader["Name"]?.ToString() ?? tenantId.ToString(),
                reader["GymCode"]?.ToString() ?? string.Empty,
                reader["PhoneNumber"]?.ToString() ?? string.Empty);
        }

        return new TenantBillingInfo(tenantId.ToString(), string.Empty, string.Empty);
    }

    private static bool IsUniqueConstraintViolation(Exception ex) =>
        ex is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601) ||
        ex is DbUpdateException { InnerException: SqlException inner } && (inner.Number == 2627 || inner.Number == 2601);

    private sealed record TenantBillingInfo(string Name, string GymCode, string PhoneNumber);
}

public class StubPlatformBillingPaymentService : IPlatformBillingPaymentService
{
    public Task<bool> HasPaymentMethodOnFileAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<PlatformPaymentAttemptResult> TryCollectInvoiceAsync(
        PlatformInvoice invoice,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PlatformPaymentAttemptResult
        {
            Success = false,
            FailureCode = "CP3_PAYMENT_NOT_WIRED"
        });

    public Task<PlatformWebhookProcessResult> HandlePaymobWebhookAsync(
        string rawPayload,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PlatformWebhookProcessResult
        {
            Success = true,
            Ignored = true,
            Message = "Stub webhook ignored."
        });

    public Task<PlatformWebhookProcessResult> HandleFawryWebhookAsync(
        string rawPayload,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PlatformWebhookProcessResult
        {
            Success = true,
            Ignored = true,
            Message = "Stub webhook ignored."
        });
}

public class ProcessSubscriptionRenewalsJob : IProcessSubscriptionRenewalsJob
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly PlatformDbContext _db;
    private readonly ISubscriptionWriteRepository _repo;
    private readonly ISubscriptionStatusCache _cache;
    private readonly IPlatformAuditService _audit;
    private readonly IPlatformInvoiceService _invoiceService;
    private readonly IPlatformBillingPaymentService _payments;
    private readonly ILogger<ProcessSubscriptionRenewalsJob> _logger;

    public ProcessSubscriptionRenewalsJob(
        PlatformDbContext db,
        ISubscriptionWriteRepository repo,
        ISubscriptionStatusCache cache,
        IPlatformAuditService audit,
        IPlatformInvoiceService invoiceService,
        IPlatformBillingPaymentService payments,
        ILogger<ProcessSubscriptionRenewalsJob> logger)
    {
        _db = db;
        _repo = repo;
        _cache = cache;
        _audit = audit;
        _invoiceService = invoiceService;
        _payments = payments;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 2, 5, 10 })]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var today = MembershipOperational.TodayCairo();
        var due = await _db.Subscriptions
            .Where(s =>
                (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trialing) &&
                s.CurrentPeriodEnd == today)
            .OrderBy(s => s.TenantId)
            .ToListAsync(cancellationToken);

        foreach (var subscription in due)
            await ProcessOneAsync(subscription, today, cancellationToken);
    }

    private async Task ProcessOneAsync(PlatformSubscription subscription, DateOnly today, CancellationToken cancellationToken)
    {
        if (subscription.Status == SubscriptionStatuses.Trialing)
        {
            var hasPaymentMethod = await _payments.HasPaymentMethodOnFileAsync(subscription.TenantId, cancellationToken);
            if (!hasPaymentMethod)
            {
                await CancelAsync(
                    subscription,
                    "trial ended without payment method on file",
                    "platform.subscription.trial_cancelled_no_payment_method",
                    cancellationToken);
                return;
            }

            var before = Snapshot(subscription);
            subscription.Status = SubscriptionStatuses.Active;
            subscription.TrialEndsAtUtc = null;

            await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                ChangeType = SubscriptionChangeTypes.Reactivation,
                FromTier = subscription.PlanTier,
                ToTier = subscription.PlanTier,
                EffectiveAtUtc = DateTime.UtcNow,
                InitiatedBy = SubscriptionInitiators.System,
                Reason = "trial converted to paid"
            }, cancellationToken);

            await AfterSubscriptionWriteAsync(
                subscription,
                "platform.subscription.trial_converted_to_active",
                before,
                cancellationToken);
        }

        if (subscription.CancelAtPeriodEnd)
        {
            await CancelAsync(
                subscription,
                "cancelled at period end",
                "platform.subscription.cancelled_at_period_end",
                cancellationToken);
            return;
        }

        var scheduledDowngrade = await _db.SubscriptionChanges
            .Where(c =>
                c.SubscriptionId == subscription.Id &&
                c.ChangeType == SubscriptionChangeTypes.Downgrade &&
                c.ToTier != null &&
                c.EffectiveAtUtc <= EndOfDayUtc(today))
            .OrderByDescending(c => c.EffectiveAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (scheduledDowngrade != null &&
            !string.Equals(subscription.PlanTier, scheduledDowngrade.ToTier, StringComparison.OrdinalIgnoreCase))
        {
            var before = Snapshot(subscription);
            subscription.PlanTier = scheduledDowngrade.ToTier!;
            subscription.PriceEgp = PlatformListPrices.ForCycle(subscription.PlanTier, subscription.BillingCycle);

            await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                ChangeType = SubscriptionChangeTypes.Downgrade,
                FromTier = scheduledDowngrade.FromTier,
                ToTier = scheduledDowngrade.ToTier,
                EffectiveAtUtc = DateTime.UtcNow,
                InitiatedBy = SubscriptionInitiators.System,
                Reason = "applied scheduled downgrade"
            }, cancellationToken);

            await AfterSubscriptionWriteAsync(
                subscription,
                "platform.subscription.downgrade_applied",
                before,
                cancellationToken);
        }

        var nextPeriodStart = subscription.CurrentPeriodEnd.AddDays(1);
        var nextPeriodEnd = AdvancePeriodEnd(nextPeriodStart, subscription.BillingCycle);
        var invoice = await _invoiceService.EnsureRenewalInvoiceAsync(
            subscription,
            nextPeriodStart,
            nextPeriodEnd,
            cancellationToken);

        if (invoice.Status == "issued")
        {
            var payment = await _payments.TryCollectInvoiceAsync(invoice, cancellationToken);
            if (payment.Success)
            {
                invoice.Status = "paid";
                invoice.PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow;
                invoice.PaymentMethod = payment.PaymentMethod;
            }
            else if (invoice.DueDate <= today)
            {
                invoice.Status = "overdue";
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        if (subscription.CurrentPeriodStart != nextPeriodStart || subscription.CurrentPeriodEnd != nextPeriodEnd)
        {
            var before = Snapshot(subscription);
            subscription.CurrentPeriodStart = nextPeriodStart;
            subscription.CurrentPeriodEnd = nextPeriodEnd;
            subscription.CancelAtPeriodEnd = false;
            if (invoice.Status is "issued" or "overdue")
                subscription.Status = SubscriptionStatuses.PastDue;
            else
                subscription.Status = SubscriptionStatuses.Active;

            await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                ChangeType = SubscriptionChangeTypes.CycleChange,
                FromTier = subscription.PlanTier,
                ToTier = subscription.PlanTier,
                EffectiveAtUtc = DateTime.UtcNow,
                InitiatedBy = SubscriptionInitiators.System,
                Reason = $"renewed via {subscription.BillingCycle} cycle"
            }, cancellationToken);

            await AfterSubscriptionWriteAsync(
                subscription,
                "platform.subscription.renewed",
                before,
                cancellationToken);
        }
    }

    private async Task CancelAsync(
        PlatformSubscription subscription,
        string reason,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var before = Snapshot(subscription);
        subscription.Status = SubscriptionStatuses.Cancelled;
        subscription.CancelledAtUtc = DateTime.UtcNow;
        subscription.CancelAtPeriodEnd = false;

        await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.Cancellation,
            FromTier = subscription.PlanTier,
            ToTier = null,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = SubscriptionInitiators.System,
            Reason = reason
        }, cancellationToken);

        await AfterSubscriptionWriteAsync(subscription, auditAction, before, cancellationToken);
    }

    private async Task AfterSubscriptionWriteAsync(
        PlatformSubscription subscription,
        string action,
        object before,
        CancellationToken cancellationToken)
    {
        await _cache.InvalidateAsync(subscription.TenantId, cancellationToken);
        await _audit.LogAsync(Guid.Empty, action, subscription.TenantId, before, Snapshot(subscription));
        _logger.LogInformation("{Action} for tenant {TenantId}", action, subscription.TenantId);
    }

    private static DateOnly AdvancePeriodEnd(DateOnly nextPeriodStart, string billingCycle)
    {
        return string.Equals(billingCycle, BillingCycles.Annual, StringComparison.OrdinalIgnoreCase)
            ? nextPeriodStart.AddYears(1).AddDays(-1)
            : nextPeriodStart.AddMonths(1).AddDays(-1);
    }

    private static DateTime EndOfDayUtc(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(23, 59, 59));
        return TimeZoneInfo.ConvertTimeToUtc(local, CairoTimeZone);
    }

    private static object Snapshot(PlatformSubscription s) => new
    {
        s.Id,
        s.TenantId,
        s.PlanTier,
        s.Status,
        s.BillingCycle,
        s.PriceEgp,
        s.CurrentPeriodStart,
        s.CurrentPeriodEnd,
        s.TrialEndsAtUtc,
        s.CancelAtPeriodEnd,
        s.CancelledAtUtc
    };
}

public class PlatformRenewalJobScheduler : IHostedService
{
    private readonly ILogger<PlatformRenewalJobScheduler> _logger;

    public PlatformRenewalJobScheduler(ILogger<PlatformRenewalJobScheduler> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        RecurringJob.AddOrUpdate<IProcessSubscriptionRenewalsJob>(
            "platform-subscription-renewals",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 2 * * *",
            new RecurringJobOptions { TimeZone = cairoTz });

        _logger.LogInformation("PlatformRenewalJobScheduler: recurring platform renewal job registered.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
