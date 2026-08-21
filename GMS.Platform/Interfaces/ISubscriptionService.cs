namespace GMS.Platform.Interfaces;

using GMS.Core.Models;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;

/// <summary>
/// Sole write gate for subscriptions: every mutation persists subscription + subscription_changes
/// in one transaction. No silent updates elsewhere.
/// </summary>
public interface ISubscriptionWriteRepository
{
    Task SaveWithChangeAsync(
        PlatformSubscription subscription,
        SubscriptionChange change,
        CancellationToken cancellationToken = default);

    Task<PlatformSubscription?> GetLiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<PlatformSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    Task<string?> GetPendingDowngradeTierAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
}

public interface ISubscriptionStatusCache
{
    Task<SubscriptionStatusDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task SetAsync(Guid tenantId, SubscriptionStatusDto status, CancellationToken cancellationToken = default);
    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>CP2 will implement real invoices; CP1 stubs the upgrade proration call.</summary>
public interface IPlatformProrationInvoiceService
{
    Task<PlatformInvoice> CreateUpgradeProrationStubAsync(
        Guid tenantId,
        Guid subscriptionId,
        decimal proratedAmountEgp,
        string fromTier,
        string toTier,
        CancellationToken cancellationToken = default);
}

public interface IPlatformInvoiceService
{
    Task<PlatformInvoice> EnsureRenewalInvoiceAsync(
        PlatformSubscription subscription,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    Task<PlatformInvoice> CreateUpgradeProrationStubAsync(
        Guid tenantId,
        Guid subscriptionId,
        decimal proratedAmountEgp,
        string fromTier,
        string toTier,
        CancellationToken cancellationToken = default);
}

public interface IPlatformBillingPaymentService
{
    Task<bool> HasPaymentMethodOnFileAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<PlatformPaymentAttemptResult> TryCollectInvoiceAsync(
        PlatformInvoice invoice,
        CancellationToken cancellationToken = default);

    Task<PlatformWebhookProcessResult> HandlePaymobWebhookAsync(
        string rawPayload,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PlatformWebhookProcessResult> HandleFawryWebhookAsync(
        string rawPayload,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public class PlatformPaymentAttemptResult
{
    public bool Success { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? ExternalReference { get; set; }
    public string? PaymentLink { get; set; }
    public string? Message { get; set; }
}

public class PlatformWebhookProcessResult
{
    public bool Success { get; set; }
    public bool Duplicate { get; set; }
    public bool Ignored { get; set; }
    public string Message { get; set; } = string.Empty;
}

public interface ISubscriptionService
{
    Task<SubscriptionMutationResult> StartTrialAsync(
        Guid tenantId,
        string tier = "growth",
        string initiatedBy = "system",
        Guid? platformAdminUserId = null,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult> ChangeTierAsync(
        Guid tenantId,
        string newTier,
        bool effectiveNow,
        string initiatedBy = "platform_admin",
        Guid? platformAdminUserId = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult> CancelAsync(
        Guid tenantId,
        bool immediate,
        string? reason,
        string initiatedBy = "platform_admin",
        Guid? platformAdminUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Hot-path read — Redis-cached; invalidated on every write.</summary>
    Task<SubscriptionStatusDto?> GetStatusAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface IProcessSubscriptionRenewalsJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
