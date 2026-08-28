namespace GMS.Platform.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;

public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionWriteRepository _repo;
    private readonly ISubscriptionStatusCache _cache;
    private readonly IFeatureAccessService _featureAccess;
    private readonly IPlatformProrationInvoiceService _prorationInvoices;
    private readonly IPlatformAuditService _audit;
    private readonly ICommercialPlanService _commercialPlans;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ISubscriptionWriteRepository repo,
        ISubscriptionStatusCache cache,
        IFeatureAccessService featureAccess,
        IPlatformProrationInvoiceService prorationInvoices,
        IPlatformAuditService audit,
        ICommercialPlanService commercialPlans,
        IConfiguration configuration,
        ILogger<SubscriptionService> logger)
    {
        _repo = repo;
        _cache = cache;
        _featureAccess = featureAccess;
        _prorationInvoices = prorationInvoices;
        _audit = audit;
        _commercialPlans = commercialPlans;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SubscriptionMutationResult> StartTrialAsync(
        Guid tenantId,
        string tier = PlanTiers.Growth,
        string initiatedBy = SubscriptionInitiators.System,
        Guid? platformAdminUserId = null,
        int? trialDays = null,
        CancellationToken cancellationToken = default)
    {
        tier = (tier ?? PlanTiers.Growth).Trim().ToLowerInvariant();
        if (!PlanTiers.IsValid(tier))
            return SubscriptionMutationResult.Fail("INVALID_TIER", $"Unknown plan tier '{tier}'.");

        var salesError = await _commercialPlans.ValidateTierForNewSalesAsync(tier, cancellationToken);
        if (salesError != null)
            return SubscriptionMutationResult.Fail("PLAN_NOT_FOR_SALES", salesError);

        var resolvedTrialDays = ResolveTrialDays(trialDays);
        if (resolvedTrialDays == null)
            return SubscriptionMutationResult.Fail("INVALID_TRIAL_DAYS", "trialDays must be between 1 and 90.");

        var existing = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (existing != null)
            return SubscriptionMutationResult.Fail(
                "LIVE_SUBSCRIPTION_EXISTS",
                "Tenant already has a live subscription (trialing/active/past_due).");

        if (await _repo.HasNonCancelledByTenantAsync(tenantId, cancellationToken))
            return SubscriptionMutationResult.Fail(
                "NON_CANCELLED_SUBSCRIPTION_EXISTS",
                "Tenant has a non-cancelled subscription (e.g. suspended). Cancel or reactivate before starting a new trial.");

        var days = resolvedTrialDays.Value;
        var today = MembershipOperational.TodayCairo();
        var trialEnds = DateTime.UtcNow.AddDays(days);
        var trialEndDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(trialEnds, EgyptTz()));

        var subscription = new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = tier,
            Status = SubscriptionStatuses.Trialing,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = await _commercialPlans.GetListPriceForCycleAsync(tier, BillingCycles.Monthly, cancellationToken),
            CurrentPeriodStart = today,
            CurrentPeriodEnd = trialEndDate,
            TrialEndsAtUtc = trialEnds,
            CancelAtPeriodEnd = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var change = new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.TrialStart,
            FromTier = null,
            ToTier = tier,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = initiatedBy,
            PlatformAdminUserId = platformAdminUserId
        };

        await _repo.SaveWithChangeAsync(subscription, change, cancellationToken);
        await AfterWriteAsync(tenantId, platformAdminUserId, "platform.subscription.trial_start", null, subscription, cancellationToken);

        return SubscriptionMutationResult.Ok(await MapAsync(subscription, cancellationToken));
    }

    public async Task<SubscriptionMutationResult> ConvertTrialToPaidAsync(
        Guid tenantId,
        string reason,
        string initiatedBy = SubscriptionInitiators.PlatformAdmin,
        Guid? platformAdminUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return SubscriptionMutationResult.Fail(
                "REASON_REQUIRED",
                "Converting a trial to paid requires a mandatory reason.");

        var subscription = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (subscription == null)
            return SubscriptionMutationResult.Fail("NO_LIVE_SUBSCRIPTION", "No live subscription for tenant.");

        if (!string.Equals(subscription.Status, SubscriptionStatuses.Trialing, StringComparison.OrdinalIgnoreCase))
            return SubscriptionMutationResult.Fail(
                "NOT_TRIALING",
                "Only trialing subscriptions can be converted to paid.");

        var before = Snapshot(subscription);
        subscription.Status = SubscriptionStatuses.Active;
        subscription.TrialEndsAtUtc = null;

        var change = new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.Reactivation,
            FromTier = subscription.PlanTier,
            ToTier = subscription.PlanTier,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = initiatedBy,
            PlatformAdminUserId = platformAdminUserId,
            Reason = reason.Trim()
        };

        await _repo.SaveWithChangeAsync(subscription, change, cancellationToken);
        await AfterWriteAsync(
            tenantId, platformAdminUserId,
            "platform.subscription.convert_trial",
            before, subscription, cancellationToken);

        return SubscriptionMutationResult.Ok(await MapAsync(subscription, cancellationToken));
    }

    public async Task<SubscriptionMutationResult> RestartPaidAsync(
        Guid tenantId,
        string tier,
        string reason,
        string initiatedBy = SubscriptionInitiators.PlatformAdmin,
        Guid? platformAdminUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return SubscriptionMutationResult.Fail(
                "REASON_REQUIRED",
                "Restarting as paid requires a mandatory reason.");

        tier = (tier ?? string.Empty).Trim().ToLowerInvariant();
        if (!PlanTiers.IsValid(tier))
            return SubscriptionMutationResult.Fail("INVALID_TIER", $"Unknown plan tier '{tier}'.");

        var salesError = await _commercialPlans.ValidateTierForNewSalesAsync(tier, cancellationToken);
        if (salesError != null)
            return SubscriptionMutationResult.Fail("PLAN_NOT_FOR_SALES", salesError);

        var live = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (live != null)
            return SubscriptionMutationResult.Fail(
                "LIVE_SUBSCRIPTION_EXISTS",
                "Tenant already has a live subscription (trialing/active/past_due).");

        var latest = await _repo.GetLatestByTenantAsync(tenantId, cancellationToken);
        if (latest == null || !string.Equals(latest.Status, SubscriptionStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            return SubscriptionMutationResult.Fail(
                "NO_CANCELLED_SUBSCRIPTION",
                "Tenant must have a cancelled subscription before restart-paid.");

        var today = MembershipOperational.TodayCairo();
        var periodEnd = AdvancePeriodEnd(today, BillingCycles.Monthly);
        var priceEgp = await _commercialPlans.GetListPriceForCycleAsync(tier, BillingCycles.Monthly, cancellationToken);

        var subscription = new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = tier,
            Status = SubscriptionStatuses.Active,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = priceEgp,
            CurrentPeriodStart = today,
            CurrentPeriodEnd = periodEnd,
            TrialEndsAtUtc = null,
            CancelAtPeriodEnd = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var change = new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.Reactivation,
            FromTier = latest.PlanTier,
            ToTier = tier,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = initiatedBy,
            PlatformAdminUserId = platformAdminUserId,
            Reason = reason.Trim()
        };

        await _repo.SaveWithChangeAsync(subscription, change, cancellationToken);
        await AfterWriteAsync(
            tenantId, platformAdminUserId,
            "platform.subscription.restart_paid",
            before: new { cancelledSubscriptionId = latest.Id, latest.Status, latest.PlanTier },
            subscription,
            cancellationToken);

        return SubscriptionMutationResult.Ok(await MapAsync(subscription, cancellationToken));
    }

    public async Task<SubscriptionMutationResult> ChangeTierAsync(
        Guid tenantId,
        string newTier,
        bool effectiveNow,
        string initiatedBy = SubscriptionInitiators.PlatformAdmin,
        Guid? platformAdminUserId = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        newTier = (newTier ?? string.Empty).Trim().ToLowerInvariant();
        if (!PlanTiers.IsValid(newTier))
            return SubscriptionMutationResult.Fail("INVALID_TIER", $"Unknown plan tier '{newTier}'.");

        var subscription = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (subscription == null)
            return SubscriptionMutationResult.Fail("NO_LIVE_SUBSCRIPTION", "No live subscription for tenant.");

        var fromTier = subscription.PlanTier;
        if (!string.Equals(fromTier, newTier, StringComparison.OrdinalIgnoreCase))
        {
            var salesError = await _commercialPlans.ValidateTierForNewSalesAsync(newTier, cancellationToken);
            if (salesError != null)
                return SubscriptionMutationResult.Fail("PLAN_NOT_FOR_SALES", salesError);
        }

        if (string.Equals(fromTier, newTier, StringComparison.OrdinalIgnoreCase))
            return SubscriptionMutationResult.Fail("SAME_TIER", "Subscription is already on that tier.");

        var fromRank = PlanTiers.Rank(fromTier);
        var toRank = PlanTiers.Rank(newTier);
        var isUpgrade = toRank > fromRank;
        var before = Snapshot(subscription);

        if (isUpgrade || effectiveNow)
        {
            subscription.PlanTier = newTier;
            subscription.PriceEgp = await _commercialPlans.GetListPriceForCycleAsync(
                newTier, subscription.BillingCycle, cancellationToken);

            var prorated = isUpgrade
                ? await EstimateProrationEgpAsync(subscription, newTier, cancellationToken)
                : (decimal?)null;

            var change = new SubscriptionChange
            {
                TenantId = tenantId,
                SubscriptionId = subscription.Id,
                ChangeType = isUpgrade ? SubscriptionChangeTypes.Upgrade : SubscriptionChangeTypes.Downgrade,
                FromTier = fromTier,
                ToTier = newTier,
                EffectiveAtUtc = DateTime.UtcNow,
                ProratedAmountEgp = prorated,
                InitiatedBy = initiatedBy,
                PlatformAdminUserId = platformAdminUserId,
                Reason = reason
            };

            await _repo.SaveWithChangeAsync(subscription, change, cancellationToken);

            if (isUpgrade && prorated is > 0)
            {
                await _prorationInvoices.CreateUpgradeProrationStubAsync(
                    tenantId, subscription.Id, prorated.Value, fromTier, newTier, cancellationToken);
            }

            await AfterWriteAsync(
                tenantId, platformAdminUserId,
                isUpgrade ? "platform.subscription.upgrade" : "platform.subscription.downgrade_now",
                before, subscription, cancellationToken);

            return SubscriptionMutationResult.Ok(await MapAsync(subscription, cancellationToken));
        }

        // Scheduled downgrade at period end — do not mutate plan_tier yet (renewal job applies).
        var periodEndUtc = PeriodEndAsUtc(subscription.CurrentPeriodEnd);
        var scheduled = new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.Downgrade,
            FromTier = fromTier,
            ToTier = newTier,
            EffectiveAtUtc = periodEndUtc,
            InitiatedBy = initiatedBy,
            PlatformAdminUserId = platformAdminUserId,
            Reason = reason
        };

        // Touch UpdatedAt via same write gate (subscription unchanged except UpdatedAt).
        await _repo.SaveWithChangeAsync(subscription, scheduled, cancellationToken);
        await AfterWriteAsync(
            tenantId, platformAdminUserId, "platform.subscription.downgrade_scheduled",
            before, subscription, cancellationToken);

        return SubscriptionMutationResult.Ok(await MapAsync(subscription, cancellationToken));
    }

    public async Task<SubscriptionMutationResult> CancelAsync(
        Guid tenantId,
        bool immediate,
        string? reason,
        string initiatedBy = SubscriptionInitiators.PlatformAdmin,
        Guid? platformAdminUserId = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (subscription == null)
            return SubscriptionMutationResult.Fail("NO_LIVE_SUBSCRIPTION", "No live subscription for tenant.");

        if (immediate && string.IsNullOrWhiteSpace(reason))
            return SubscriptionMutationResult.Fail(
                "REASON_REQUIRED",
                "Immediate cancellation requires a mandatory reason (fraud, ToS, etc.).");

        var before = Snapshot(subscription);

        if (immediate)
        {
            subscription.Status = SubscriptionStatuses.Cancelled;
            subscription.CancelledAtUtc = DateTime.UtcNow;
            subscription.CancelAtPeriodEnd = false;
        }
        else
        {
            subscription.CancelAtPeriodEnd = true;
        }

        var change = new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.Cancellation,
            FromTier = subscription.PlanTier,
            ToTier = null,
            EffectiveAtUtc = immediate
                ? DateTime.UtcNow
                : PeriodEndAsUtc(subscription.CurrentPeriodEnd),
            InitiatedBy = initiatedBy,
            PlatformAdminUserId = platformAdminUserId,
            Reason = reason
        };

        await _repo.SaveWithChangeAsync(subscription, change, cancellationToken);
        await AfterWriteAsync(
            tenantId, platformAdminUserId,
            immediate ? "platform.subscription.cancel_immediate" : "platform.subscription.cancel_at_period_end",
            before, subscription, cancellationToken);

        return SubscriptionMutationResult.Ok(await MapAsync(subscription, cancellationToken));
    }

    public async Task<SubscriptionMutationResult> UndoCancelAtPeriodEndAsync(
        Guid tenantId,
        string reason,
        string initiatedBy = SubscriptionInitiators.PlatformAdmin,
        Guid? platformAdminUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return SubscriptionMutationResult.Fail(
                "REASON_REQUIRED",
                "Undoing a scheduled cancellation requires a mandatory reason.");

        var subscription = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (subscription == null)
            return SubscriptionMutationResult.Fail("NO_LIVE_SUBSCRIPTION", "No live subscription for tenant.");

        if (!subscription.CancelAtPeriodEnd)
            return SubscriptionMutationResult.Fail(
                "CANCEL_NOT_SCHEDULED",
                "Subscription does not have a cancellation scheduled at period end.");

        var before = Snapshot(subscription);
        subscription.CancelAtPeriodEnd = false;

        var change = new SubscriptionChange
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            ChangeType = SubscriptionChangeTypes.CancelUndo,
            FromTier = subscription.PlanTier,
            ToTier = subscription.PlanTier,
            EffectiveAtUtc = DateTime.UtcNow,
            InitiatedBy = initiatedBy,
            PlatformAdminUserId = platformAdminUserId,
            Reason = reason.Trim()
        };

        await _repo.SaveWithChangeAsync(subscription, change, cancellationToken);
        await AfterWriteAsync(
            tenantId, platformAdminUserId,
            "platform.subscription.undo_cancel_at_period_end",
            before, subscription, cancellationToken);

        return SubscriptionMutationResult.Ok(await MapAsync(subscription, cancellationToken));
    }

    public async Task<SubscriptionStatusDto?> GetStatusAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetAsync(tenantId, cancellationToken);
        if (cached != null)
            return cached;

        var subscription = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (subscription == null)
            return null;

        var dto = await MapAsync(subscription, cancellationToken);
        await _cache.SetAsync(tenantId, dto, cancellationToken);
        return dto;
    }

    private async Task AfterWriteAsync(
        Guid tenantId,
        Guid? actorId,
        string action,
        object? before,
        PlatformSubscription after,
        CancellationToken cancellationToken)
    {
        await _cache.InvalidateAsync(tenantId, cancellationToken);
        await _featureAccess.InvalidateAsync(tenantId, cancellationToken);

        if (actorId.HasValue)
        {
            await _audit.LogAsync(actorId.Value, action, tenantId, before, Snapshot(after));
        }
        else
        {
            // System writes still audit with a well-known zero actor when no admin is present.
            await _audit.LogAsync(Guid.Empty, action, tenantId, before, Snapshot(after));
        }

        _logger.LogInformation("Subscription write {Action} for tenant {TenantId}", action, tenantId);
    }

    private async Task<SubscriptionStatusDto> MapAsync(PlatformSubscription s, CancellationToken ct)
    {
        return new SubscriptionStatusDto
        {
            Id = s.Id,
            TenantId = s.TenantId,
            PlanTier = s.PlanTier,
            Status = s.Status,
            BillingCycle = s.BillingCycle,
            PriceEgp = s.PriceEgp,
            CurrentPeriodStart = s.CurrentPeriodStart,
            CurrentPeriodEnd = s.CurrentPeriodEnd,
            TrialEndsAtUtc = s.TrialEndsAtUtc,
            CancelAtPeriodEnd = s.CancelAtPeriodEnd,
            CancelledAtUtc = s.CancelledAtUtc,
            SuspendedAtUtc = s.SuspendedAtUtc,
            UpdatedAtUtc = s.UpdatedAtUtc,
            PendingDowngradeTier = await _repo.GetPendingDowngradeTierAsync(s.Id, ct),
            HasPaymentMethodOnFile = !string.IsNullOrWhiteSpace(s.SavedCardToken)
        };
    }

    private int? ResolveTrialDays(int? trialDays)
    {
        if (!trialDays.HasValue)
            return _configuration.GetValue("PlatformSubscription:TrialDays", 14);
        if (trialDays.Value < 1 || trialDays.Value > 90)
            return null;
        return trialDays.Value;
    }

    private static DateOnly AdvancePeriodEnd(DateOnly periodStart, string billingCycle) =>
        string.Equals(billingCycle, BillingCycles.Annual, StringComparison.OrdinalIgnoreCase)
            ? periodStart.AddYears(1).AddDays(-1)
            : periodStart.AddMonths(1).AddDays(-1);

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

    private async Task<decimal> EstimateProrationEgpAsync(
        PlatformSubscription subscription,
        string newTier,
        CancellationToken cancellationToken)
    {
        var today = MembershipOperational.TodayCairo();
        var totalDays = Math.Max(1, subscription.CurrentPeriodEnd.DayNumber - subscription.CurrentPeriodStart.DayNumber);
        var remaining = Math.Max(0, subscription.CurrentPeriodEnd.DayNumber - today.DayNumber);
        var oldPrice = subscription.PriceEgp;
        var newPrice = await _commercialPlans.GetListPriceForCycleAsync(
            newTier, subscription.BillingCycle, cancellationToken);
        var delta = newPrice - oldPrice;
        if (delta <= 0)
            return 0m;
        return Math.Round(delta * remaining / totalDays, 2, MidpointRounding.AwayFromZero);
    }

    private static DateTime PeriodEndAsUtc(DateOnly periodEnd)
    {
        var local = periodEnd.ToDateTime(new TimeOnly(23, 59, 59));
        return TimeZoneInfo.ConvertTimeToUtc(local, EgyptTz());
    }

    private static TimeZoneInfo EgyptTz() =>
        TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
}
