namespace GMS.Platform.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Platform invoice dunning: T+0 reminder → T+2 reminder → T+5 past_due + escalate → grace → suspended.
/// SubjectType = platform_invoice; SubjectId = platform_invoices.Id.
/// </summary>
public class PlatformInvoiceDunningHandler : IAutomationSequenceHandler
{
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    public string SequenceKey => AutomationSequenceKeys.PlatformInvoiceDunning;

    private readonly PlatformDbContext _db;
    private readonly ISubscriptionWriteRepository _repo;
    private readonly ISubscriptionStatusCache _statusCache;
    private readonly IFeatureAccessService _featureAccess;
    private readonly IPlatformAuditService _audit;
    private readonly IWhatsAppService _whatsApp;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache _distributedCache;
    private readonly ILogger<PlatformInvoiceDunningHandler> _logger;

    public PlatformInvoiceDunningHandler(
        PlatformDbContext db,
        ISubscriptionWriteRepository repo,
        ISubscriptionStatusCache statusCache,
        IFeatureAccessService featureAccess,
        IPlatformAuditService audit,
        IWhatsAppService whatsApp,
        IConfiguration configuration,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache distributedCache,
        ILogger<PlatformInvoiceDunningHandler> logger)
    {
        _db = db;
        _repo = repo;
        _statusCache = statusCache;
        _featureAccess = featureAccess;
        _audit = audit;
        _whatsApp = whatsApp;
        _configuration = configuration;
        _distributedCache = distributedCache;
        _logger = logger;
    }

    public async Task<AutomationStepResult> ExecuteStepAsync(
        AutomationEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _db.PlatformInvoices
            .FirstOrDefaultAsync(i => i.Id == enrollment.SubjectId, cancellationToken);

        if (invoice == null)
            return AutomationStepResult.HaltNow(AutomationHaltReasons.Cancelled);

        if (string.Equals(invoice.Status, "paid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invoice.Status, "voided", StringComparison.OrdinalIgnoreCase))
            return AutomationStepResult.HaltNow(AutomationHaltReasons.Paid);

        return enrollment.Step switch
        {
            PlatformInvoiceDunningSteps.DueReminder => await StepDueReminderAsync(enrollment, invoice, cancellationToken),
            PlatformInvoiceDunningSteps.SecondReminder => await StepSecondReminderAsync(enrollment, invoice, cancellationToken),
            PlatformInvoiceDunningSteps.MarkPastDue => await StepMarkPastDueAsync(enrollment, invoice, cancellationToken),
            PlatformInvoiceDunningSteps.Suspend => await StepSuspendAsync(enrollment, invoice, cancellationToken),
            _ => AutomationStepResult.Complete()
        };
    }

    private async Task<AutomationStepResult> StepDueReminderAsync(
        AutomationEnrollment enrollment,
        PlatformInvoice invoice,
        CancellationToken cancellationToken)
    {
        await SendReminderAsync(invoice, "platform_invoice_due", "Invoice is due today", cancellationToken);
        return AutomationStepResult.Schedule(
            PlatformInvoiceDunningSteps.SecondReminder,
            AtDuePlusDaysUtc(invoice.DueDate, 2));
    }

    private async Task<AutomationStepResult> StepSecondReminderAsync(
        AutomationEnrollment enrollment,
        PlatformInvoice invoice,
        CancellationToken cancellationToken)
    {
        await SendReminderAsync(invoice, "platform_invoice_reminder", "Invoice payment reminder", cancellationToken);
        return AutomationStepResult.Schedule(
            PlatformInvoiceDunningSteps.MarkPastDue,
            AtDuePlusDaysUtc(invoice.DueDate, 5));
    }

    private async Task<AutomationStepResult> StepMarkPastDueAsync(
        AutomationEnrollment enrollment,
        PlatformInvoice invoice,
        CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId, cancellationToken);

        if (subscription != null &&
            subscription.Status is SubscriptionStatuses.Active or SubscriptionStatuses.Trialing or SubscriptionStatuses.PastDue)
        {
            if (subscription.Status != SubscriptionStatuses.PastDue)
            {
                var before = Snapshot(subscription);
                subscription.Status = SubscriptionStatuses.PastDue;
                subscription.UpdatedAtUtc = DateTime.UtcNow;

                await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
                {
                    TenantId = subscription.TenantId,
                    SubscriptionId = subscription.Id,
                    ChangeType = SubscriptionChangeTypes.PastDue,
                    FromTier = subscription.PlanTier,
                    ToTier = subscription.PlanTier,
                    EffectiveAtUtc = DateTime.UtcNow,
                    InitiatedBy = SubscriptionInitiators.System,
                    Reason = $"dunning T+5 unpaid invoice {invoice.InvoiceNumber}"
                }, cancellationToken);

                await AfterStatusWriteAsync(subscription, "platform.subscription.past_due", before, cancellationToken);
            }
        }

        await SendReminderAsync(invoice, "platform_invoice_past_due", "Invoice past due — escalating", cancellationToken);

        var graceDays = _configuration.GetValue("PlatformBilling:DunningGraceDaysAfterPastDue", 5);
        return AutomationStepResult.Schedule(
            PlatformInvoiceDunningSteps.Suspend,
            DateTime.UtcNow.AddDays(graceDays));
    }

    private async Task<AutomationStepResult> StepSuspendAsync(
        AutomationEnrollment enrollment,
        PlatformInvoice invoice,
        CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId, cancellationToken);

        if (subscription != null && subscription.Status != SubscriptionStatuses.Cancelled)
        {
            if (subscription.Status != SubscriptionStatuses.Suspended)
            {
                var before = Snapshot(subscription);
                subscription.Status = SubscriptionStatuses.Suspended;
                subscription.SuspendedAtUtc = DateTime.UtcNow;
                subscription.UpdatedAtUtc = DateTime.UtcNow;

                await _repo.SaveWithChangeAsync(subscription, new SubscriptionChange
                {
                    TenantId = subscription.TenantId,
                    SubscriptionId = subscription.Id,
                    ChangeType = SubscriptionChangeTypes.Suspension,
                    FromTier = subscription.PlanTier,
                    ToTier = subscription.PlanTier,
                    EffectiveAtUtc = DateTime.UtcNow,
                    InitiatedBy = SubscriptionInitiators.System,
                    Reason = $"dunning grace expired unpaid invoice {invoice.InvoiceNumber}"
                }, cancellationToken);

                await AfterStatusWriteAsync(subscription, "platform.subscription.suspended", before, cancellationToken);
            }
        }

        await SendReminderAsync(invoice, "platform_invoice_suspended", "Account suspended for non-payment", cancellationToken);
        return AutomationStepResult.Complete(AutomationHaltReasons.Completed);
    }

    private async Task SendReminderAsync(
        PlatformInvoice invoice,
        string template,
        string fallbackCaption,
        CancellationToken cancellationToken)
    {
        var billing = await LoadTenantBillingAsync(invoice.TenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(billing.Phone))
        {
            _logger.LogWarning(
                "Dunning WhatsApp skipped for invoice {InvoiceNumber}: no tenant phone.",
                invoice.InvoiceNumber);
            return;
        }

        try
        {
            await _whatsApp.SendTemplateAsync(billing.Phone, template, new Dictionary<string, string>
            {
                ["invoiceNumber"] = invoice.InvoiceNumber,
                ["amount"] = invoice.Total.ToString("F2"),
                ["dueDate"] = invoice.DueDate.ToString("yyyy-MM-dd"),
                ["gymName"] = billing.Name,
                ["caption"] = fallbackCaption
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dunning WhatsApp failed for invoice {InvoiceNumber}", invoice.InvoiceNumber);
        }
    }

    private async Task AfterStatusWriteAsync(
        PlatformSubscription subscription,
        string auditAction,
        object before,
        CancellationToken cancellationToken)
    {
        await _statusCache.InvalidateAsync(subscription.TenantId, cancellationToken);
        await _featureAccess.InvalidateAsync(subscription.TenantId, cancellationToken);
        await SubscriptionAccessService.InvalidateAsync(_distributedCache, subscription.TenantId, cancellationToken);
        await _audit.LogAsync(Guid.Empty, auditAction, subscription.TenantId, before, Snapshot(subscription));
    }

    private async Task<(string Name, string Phone)> LoadTenantBillingAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return (tenantId.ToString(), string.Empty);

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) Name, PhoneNumber FROM dbo.tenants WHERE Id = @tenantId";
        var p = command.CreateParameter();
        p.ParameterName = "@tenantId";
        p.Value = tenantId;
        command.Parameters.Add(p);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader["Name"]?.ToString() ?? tenantId.ToString(),
                reader["PhoneNumber"]?.ToString() ?? string.Empty);
        }

        return (tenantId.ToString(), string.Empty);
    }

    /// <summary>DueDate (Cairo calendar day) + N days → UTC for NextRunAt.</summary>
    public static DateTime AtDuePlusDaysUtc(DateOnly dueDate, int days)
    {
        var local = dueDate.AddDays(days).ToDateTime(TimeOnly.MinValue);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), CairoTz);
    }

    private static object Snapshot(PlatformSubscription s) => new
    {
        s.Status,
        s.SuspendedAtUtc,
        s.CurrentPeriodStart,
        s.CurrentPeriodEnd
    };
}
