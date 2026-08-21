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
    private readonly IConfiguration _configuration;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ISubscriptionWriteRepository repo,
        ISubscriptionStatusCache cache,
        IFeatureAccessService featureAccess,
        IPlatformProrationInvoiceService prorationInvoices,
        IPlatformAuditService audit,
        IConfiguration configuration,
        ILogger<SubscriptionService> logger)
    {
        _repo = repo;
        _cache = cache;
        _featureAccess = featureAccess;
        _prorationInvoices = prorationInvoices;
        _audit = audit;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SubscriptionMutationResult> StartTrialAsync(
        Guid tenantId,
        string tier = PlanTiers.Growth,
        string initiatedBy = SubscriptionInitiators.System,
        Guid? platformAdminUserId = null,
        CancellationToken cancellationToken = default)
    {
        tier = (tier ?? PlanTiers.Growth).Trim().ToLowerInvariant();
        if (!PlanTiers.IsValid(tier))
            return SubscriptionMutationResult.Fail("INVALID_TIER", $"Unknown plan tier '{tier}'.");

        var existing = await _repo.GetLiveByTenantAsync(tenantId, cancellationToken);
        if (existing != null)
            return SubscriptionMutationResult.Fail(
                "LIVE_SUBSCRIPTION_EXISTS",
                "Tenant already has a live subscription (trialing/active/past_due).");

        var trialDays = _configuration.GetValue("PlatformSubscription:TrialDays", 14);
        var today = MembershipOperational.TodayCairo();
        var trialEnds = DateTime.UtcNow.AddDays(trialDays);
        var trialEndDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(trialEnds, EgyptTz()));

        var subscription = new PlatformSubscription
        {
            TenantId = tenantId,
            PlanTier = tier,
            Status = SubscriptionStatuses.Trialing,
            BillingCycle = BillingCycles.Monthly,
            PriceEgp = PlatformListPrices.ForCycle(tier, BillingCycles.Monthly),
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
        if (string.Equals(fromTier, newTier, StringComparison.OrdinalIgnoreCase))
            return SubscriptionMutationResult.Fail("SAME_TIER", "Subscription is already on that tier.");

        var fromRank = PlanTiers.Rank(fromTier);
        var toRank = PlanTiers.Rank(newTier);
        var isUpgrade = toRank > fromRank;
        var before = Snapshot(subscription);

        if (isUpgrade || effectiveNow)
        {
            var prorated = isUpgrade
                ? EstimateProrationEgp(subscription, newTier)
                : (decimal?)null;

            subscription.PlanTier = newTier;
            subscription.PriceEgp = PlatformListPrices.ForCycle(newTier, subscription.BillingCycle);

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
            PendingDowngradeTier = await _repo.GetPendingDowngradeTierAsync(s.Id, ct)
        };
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

    /// <summary>Simple remaining-days / period-days * delta price. CP2 may refine.</summary>
    private static decimal EstimateProrationEgp(PlatformSubscription subscription, string newTier)
    {
        var today = MembershipOperational.TodayCairo();
        var totalDays = Math.Max(1, subscription.CurrentPeriodEnd.DayNumber - subscription.CurrentPeriodStart.DayNumber);
        var remaining = Math.Max(0, subscription.CurrentPeriodEnd.DayNumber - today.DayNumber);
        var oldPrice = subscription.PriceEgp;
        var newPrice = PlatformListPrices.ForCycle(newTier, subscription.BillingCycle);
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
