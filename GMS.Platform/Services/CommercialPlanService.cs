namespace GMS.Platform.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Platform.Constants;
using GMS.Platform.DTOs;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

public class CommercialPlanService : ICommercialPlanService
{
    private static readonly HashSet<string> PricingUiLiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionStatuses.Trialing,
        SubscriptionStatuses.Active,
        SubscriptionStatuses.PastDue,
        SubscriptionStatuses.Suspended
    };

    private static readonly HashSet<string> ModuleFeatureKeys = new(FeatureKeys.PhaseAModules, StringComparer.OrdinalIgnoreCase);

    private readonly PlatformDbContext _db;
    private readonly IPlatformAuditService _audit;
    private readonly ILogger<CommercialPlanService> _logger;

    public CommercialPlanService(
        PlatformDbContext db,
        IPlatformAuditService audit,
        ILogger<CommercialPlanService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CommercialPlanListItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _db.CommercialPlans.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Tier)
            .ToListAsync(cancellationToken);

        return await MapListAsync(plans, cancellationToken);
    }

    public async Task<CommercialPlanDetailDto?> GetAsync(string tier, CancellationToken cancellationToken = default)
    {
        tier = NormalizeTier(tier);
        if (!PlanTiers.IsValid(tier))
            return null;

        var plan = await _db.CommercialPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Tier == tier, cancellationToken);
        if (plan == null)
            return null;

        var list = await MapListAsync([plan], cancellationToken);
        var item = list[0];
        var features = await LoadEnabledModuleFeaturesAsync(tier, cancellationToken);

        return new CommercialPlanDetailDto
        {
            Tier = item.Tier,
            DisplayName = item.DisplayName,
            Description = item.Description,
            SortOrder = item.SortOrder,
            IsActiveForSales = item.IsActiveForSales,
            IsDefault = item.IsDefault,
            MonthlyPriceEgp = item.MonthlyPriceEgp,
            AnnualPriceEgp = item.AnnualPriceEgp,
            AnnualSavingsPercent = item.AnnualSavingsPercent,
            MembersCap = item.MembersCap,
            StaffCap = item.StaffCap,
            BranchesCap = item.BranchesCap,
            WhatsAppCap = item.WhatsAppCap,
            FeatureCount = item.FeatureCount,
            LiveSubscriptionCount = item.LiveSubscriptionCount,
            UpdatedAtUtc = item.UpdatedAtUtc,
            EnabledFeatures = features
        };
    }

    public async Task<string> GetDefaultTierAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _db.CommercialPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsDefault, cancellationToken);
        if (plan != null)
            return plan.Tier;

        return PlanTiers.Growth;
    }

    public async Task<decimal> GetListPriceForCycleAsync(string tier, string cycle, CancellationToken cancellationToken = default)
    {
        tier = NormalizeTier(tier);
        var monthly = await GetMonthlyListPriceAsync(tier, cancellationToken);
        return ForCycleFromMonthly(monthly, cycle);
    }

    public async Task<bool> IsActiveForSalesAsync(string tier, CancellationToken cancellationToken = default)
    {
        tier = NormalizeTier(tier);
        var plan = await _db.CommercialPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Tier == tier, cancellationToken);
        if (plan == null)
            return true;

        return plan.IsActiveForSales;
    }

    public async Task<string?> ValidateTierForNewSalesAsync(string tier, CancellationToken cancellationToken = default)
    {
        tier = NormalizeTier(tier);
        if (!PlanTiers.IsValid(tier))
            return $"Unknown plan tier '{tier}'.";

        if (!await IsActiveForSalesAsync(tier, cancellationToken))
            return $"Plan tier '{tier}' is not available for new sales.";

        return null;
    }

    public async Task<CommercialPlanMutationResult> UpdateMetadataAsync(
        string tier,
        UpdatePlanMetadataRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateReason(request.Reason);
        if (validation != null)
            return CommercialPlanMutationResult.Fail("REASON_REQUIRED", validation);

        var plan = await RequirePlanAsync(tier, cancellationToken);
        if (plan == null)
            return CommercialPlanMutationResult.Fail("PLAN_NOT_FOUND", "Plan not found.");

        var before = SnapshotMetadata(plan);
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            plan.DisplayName = request.DisplayName.Trim();
        plan.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        plan.SortOrder = request.SortOrder;
        plan.UpdatedAtUtc = DateTime.UtcNow;

        await LogFieldChangesAsync(plan.Tier, actorPlatformUserId, request.Reason.Trim(), before, SnapshotMetadata(plan), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.plan.metadata_changed",
            tenantId: null,
            before,
            SnapshotMetadata(plan),
            ipAddress);

        return CommercialPlanMutationResult.Ok((await GetAsync(plan.Tier, cancellationToken))!);
    }

    public async Task<CommercialPlanMutationResult> UpdatePricingAsync(
        string tier,
        UpdatePlanPricingRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateReason(request.Reason);
        if (validation != null)
            return CommercialPlanMutationResult.Fail("REASON_REQUIRED", validation);

        if (request.MonthlyPriceEgp <= 0)
            return CommercialPlanMutationResult.Fail("INVALID_PRICE", "Monthly price must be greater than zero.");

        var plan = await RequirePlanAsync(tier, cancellationToken);
        if (plan == null)
            return CommercialPlanMutationResult.Fail("PLAN_NOT_FOUND", "Plan not found.");

        var oldMonthly = plan.MonthlyPriceEgp;
        if (oldMonthly == request.MonthlyPriceEgp)
            return CommercialPlanMutationResult.Ok((await GetAsync(plan.Tier, cancellationToken))!);

        plan.MonthlyPriceEgp = request.MonthlyPriceEgp;
        plan.UpdatedAtUtc = DateTime.UtcNow;

        await AppendChangeLogAsync(
            plan.Tier,
            "monthly_price_egp",
            oldMonthly.ToString("0.##"),
            request.MonthlyPriceEgp.ToString("0.##"),
            actorPlatformUserId,
            request.Reason.Trim(),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.plan.price_changed",
            tenantId: null,
            before: new { tier = plan.Tier, monthlyPriceEgp = oldMonthly, annualPriceEgp = AnnualFromMonthly(oldMonthly) },
            after: new { tier = plan.Tier, monthlyPriceEgp = plan.MonthlyPriceEgp, annualPriceEgp = AnnualFromMonthly(plan.MonthlyPriceEgp) },
            ipAddress);

        _logger.LogInformation(
            "Commercial list price for {Tier} changed {Old} -> {New} by {Actor}",
            plan.Tier, oldMonthly, plan.MonthlyPriceEgp, actorPlatformUserId);

        return CommercialPlanMutationResult.Ok((await GetAsync(plan.Tier, cancellationToken))!);
    }

    public async Task<CommercialPlanMutationResult> UpdateCapsAsync(
        string tier,
        UpdatePlanCapsRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateReason(request.Reason);
        if (validation != null)
            return CommercialPlanMutationResult.Fail("REASON_REQUIRED", validation);

        tier = NormalizeTier(tier);
        if (!PlanTiers.IsValid(tier))
            return CommercialPlanMutationResult.Fail("INVALID_TIER", $"Unknown plan tier '{tier}'.");

        _ = await RequirePlanAsync(tier, cancellationToken);

        var before = await LoadCapsSnapshotAsync(tier, cancellationToken);
        await UpsertCapAsync(tier, UsageMetrics.ActiveMembers, request.ActiveMembers, cancellationToken);
        await UpsertCapAsync(tier, UsageMetrics.StaffSeats, request.StaffSeats, cancellationToken);
        await UpsertCapAsync(tier, UsageMetrics.Branches, request.Branches, cancellationToken);
        await UpsertCapAsync(tier, UsageMetrics.WhatsAppMessages, request.WhatsAppMessages, cancellationToken);

        var after = await LoadCapsSnapshotAsync(tier, cancellationToken);
        await LogFieldChangesAsync(tier, actorPlatformUserId, request.Reason.Trim(), before, after, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.plan.cap_changed",
            tenantId: null,
            before,
            after,
            ipAddress);

        return CommercialPlanMutationResult.Ok((await GetAsync(tier, cancellationToken))!);
    }

    public async Task<CommercialPlanMutationResult> UpdateFeaturesAsync(
        string tier,
        UpdatePlanFeaturesRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateReason(request.Reason);
        if (validation != null)
            return CommercialPlanMutationResult.Fail("REASON_REQUIRED", validation);

        tier = NormalizeTier(tier);
        if (!PlanTiers.IsValid(tier))
            return CommercialPlanMutationResult.Fail("INVALID_TIER", $"Unknown plan tier '{tier}'.");

        _ = await RequirePlanAsync(tier, cancellationToken);

        var requested = request.EnabledFeatures
            .Select(f => f.Trim().ToLowerInvariant())
            .Where(f => ModuleFeatureKeys.Contains(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var before = await LoadEnabledModuleFeaturesAsync(tier, cancellationToken);
        var existingRows = await _db.TierFeatureMaps
            .Where(m => m.Tier == tier && ModuleFeatureKeys.Contains(m.FeatureKey))
            .ToListAsync(cancellationToken);

        foreach (var row in existingRows.Where(r => !requested.Contains(r.FeatureKey)))
            _db.TierFeatureMaps.Remove(row);

        var existingKeys = existingRows.Select(r => r.FeatureKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in requested.Where(k => !existingKeys.Contains(k)))
        {
            _db.TierFeatureMaps.Add(new TierFeatureMap
            {
                Tier = tier,
                FeatureKey = key,
                CapValue = null
            });
        }

        var after = requested.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        await AppendChangeLogAsync(
            tier,
            "enabled_features",
            string.Join(',', before),
            string.Join(',', after),
            actorPlatformUserId,
            request.Reason.Trim(),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.plan.features_changed",
            tenantId: null,
            before: new { tier, features = before },
            after: new { tier, features = after },
            ipAddress);

        return CommercialPlanMutationResult.Ok((await GetAsync(tier, cancellationToken))!);
    }

    public async Task<CommercialPlanMutationResult> SetSalesStatusAsync(
        string tier,
        UpdatePlanSalesStatusRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateReason(request.Reason);
        if (validation != null)
            return CommercialPlanMutationResult.Fail("REASON_REQUIRED", validation);

        var plan = await RequirePlanAsync(tier, cancellationToken);
        if (plan == null)
            return CommercialPlanMutationResult.Fail("PLAN_NOT_FOUND", "Plan not found.");

        if (plan.IsDefault && !request.IsActiveForSales)
            return CommercialPlanMutationResult.Fail("DEFAULT_PLAN_ACTIVE", "Cannot deactivate the default plan for new sales. Set another default first.");

        var old = plan.IsActiveForSales;
        if (old == request.IsActiveForSales)
            return CommercialPlanMutationResult.Ok((await GetAsync(plan.Tier, cancellationToken))!);

        plan.IsActiveForSales = request.IsActiveForSales;
        plan.UpdatedAtUtc = DateTime.UtcNow;

        await AppendChangeLogAsync(
            plan.Tier,
            "is_active_for_sales",
            old.ToString(),
            request.IsActiveForSales.ToString(),
            actorPlatformUserId,
            request.Reason.Trim(),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            request.IsActiveForSales ? "platform.plan.activated_for_sales" : "platform.plan.deactivated_for_sales",
            tenantId: null,
            before: new { tier = plan.Tier, isActiveForSales = old },
            after: new { tier = plan.Tier, isActiveForSales = plan.IsActiveForSales },
            ipAddress);

        return CommercialPlanMutationResult.Ok((await GetAsync(plan.Tier, cancellationToken))!);
    }

    public async Task<CommercialPlanMutationResult> SetDefaultAsync(
        string tier,
        SetDefaultPlanRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateReason(request.Reason);
        if (validation != null)
            return CommercialPlanMutationResult.Fail("REASON_REQUIRED", validation);

        tier = NormalizeTier(tier);
        var plan = await RequirePlanAsync(tier, cancellationToken);
        if (plan == null)
            return CommercialPlanMutationResult.Fail("PLAN_NOT_FOUND", "Plan not found.");

        if (!plan.IsActiveForSales)
            return CommercialPlanMutationResult.Fail("PLAN_NOT_FOR_SALES", "Default plan must be active for new sales.");

        if (plan.IsDefault)
            return CommercialPlanMutationResult.Ok((await GetAsync(plan.Tier, cancellationToken))!);

        var previousDefault = await _db.CommercialPlans.FirstOrDefaultAsync(p => p.IsDefault, cancellationToken);
        var oldDefaultTier = previousDefault?.Tier;

        if (previousDefault != null)
            previousDefault.IsDefault = false;

        plan.IsDefault = true;
        plan.UpdatedAtUtc = DateTime.UtcNow;

        await AppendChangeLogAsync(
            plan.Tier,
            "is_default",
            oldDefaultTier,
            plan.Tier,
            actorPlatformUserId,
            request.Reason.Trim(),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            actorPlatformUserId,
            "platform.plan.default_changed",
            tenantId: null,
            before: new { defaultTier = oldDefaultTier },
            after: new { defaultTier = plan.Tier },
            ipAddress);

        return CommercialPlanMutationResult.Ok((await GetAsync(plan.Tier, cancellationToken))!);
    }

    public async Task<PlatformPagedResult<PlanChangeLogDto>> GetHistoryAsync(
        string tier,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        tier = NormalizeTier(tier);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);

        var query = _db.Set<PlanChangeLog>().AsNoTracking()
            .Where(l => l.Tier == tier);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var actorIds = rows.Select(r => r.ActorPlatformUserId).Distinct().ToList();
        var actors = await _db.PlatformAdminUsers.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return new PlatformPagedResult<PlanChangeLogDto>
        {
            Items = rows.Select(r => new PlanChangeLogDto
            {
                Id = r.Id,
                Tier = r.Tier,
                FieldName = r.FieldName,
                OldValue = r.OldValue,
                NewValue = r.NewValue,
                ActorPlatformUserId = r.ActorPlatformUserId,
                ActorName = actors.TryGetValue(r.ActorPlatformUserId, out var name) ? name : null,
                Reason = r.Reason,
                CreatedAtUtc = r.CreatedAtUtc
            }).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    internal static decimal AnnualFromMonthly(decimal monthly) => monthly * 10m;

    internal static decimal AnnualSavingsPercent(decimal monthly)
    {
        var fullYear = monthly * 12m;
        if (fullYear <= 0)
            return 0m;
        var annual = monthly * 10m;
        return Math.Round((1m - annual / fullYear) * 100m, 0, MidpointRounding.AwayFromZero);
    }

    internal static decimal ForCycleFromMonthly(decimal monthly, string cycle) =>
        string.Equals(cycle, BillingCycles.Annual, StringComparison.OrdinalIgnoreCase)
            ? AnnualFromMonthly(monthly)
            : monthly;

    private async Task<decimal> GetMonthlyListPriceAsync(string tier, CancellationToken cancellationToken)
    {
        var plan = await _db.CommercialPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Tier == tier, cancellationToken);
        return plan?.MonthlyPriceEgp ?? PlatformListPrices.MonthlyEgp(tier);
    }

    private async Task<List<CommercialPlanListItemDto>> MapListAsync(
        IReadOnlyList<CommercialPlan> plans,
        CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
            return [];

        var tiers = plans.Select(p => p.Tier).ToList();
        var featureMaps = await _db.TierFeatureMaps.AsNoTracking()
            .Where(m => tiers.Contains(m.Tier))
            .ToListAsync(cancellationToken);

        var liveCounts = await _db.Subscriptions.AsNoTracking()
            .Where(s => PricingUiLiveStatuses.Contains(s.Status))
            .GroupBy(s => s.PlanTier)
            .Select(g => new { Tier = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var liveByTier = liveCounts.ToDictionary(
            x => x.Tier.Trim().ToLowerInvariant(),
            x => x.Count,
            StringComparer.OrdinalIgnoreCase);

        return plans.Select(plan =>
        {
            var tierMaps = featureMaps.Where(m => string.Equals(m.Tier, plan.Tier, StringComparison.OrdinalIgnoreCase)).ToList();
            var caps = tierMaps.Where(m => UsageMetrics.All.Contains(m.FeatureKey)).ToDictionary(m => m.FeatureKey, m => m.CapValue);
            var moduleCount = tierMaps.Count(m => ModuleFeatureKeys.Contains(m.FeatureKey));

            return new CommercialPlanListItemDto
            {
                Tier = plan.Tier,
                DisplayName = plan.DisplayName,
                Description = plan.Description,
                SortOrder = plan.SortOrder,
                IsActiveForSales = plan.IsActiveForSales,
                IsDefault = plan.IsDefault,
                MonthlyPriceEgp = plan.MonthlyPriceEgp,
                AnnualPriceEgp = AnnualFromMonthly(plan.MonthlyPriceEgp),
                AnnualSavingsPercent = AnnualSavingsPercent(plan.MonthlyPriceEgp),
                MembersCap = caps.GetValueOrDefault(UsageMetrics.ActiveMembers),
                StaffCap = caps.GetValueOrDefault(UsageMetrics.StaffSeats),
                BranchesCap = caps.GetValueOrDefault(UsageMetrics.Branches),
                WhatsAppCap = caps.GetValueOrDefault(UsageMetrics.WhatsAppMessages),
                FeatureCount = moduleCount,
                LiveSubscriptionCount = liveByTier.GetValueOrDefault(plan.Tier),
                UpdatedAtUtc = plan.UpdatedAtUtc
            };
        }).ToList();
    }

    private async Task<CommercialPlan?> RequirePlanAsync(string tier, CancellationToken cancellationToken)
    {
        tier = NormalizeTier(tier);
        return await _db.CommercialPlans.FirstOrDefaultAsync(p => p.Tier == tier, cancellationToken);
    }

    private async Task UpsertCapAsync(string tier, string metric, int? cap, CancellationToken cancellationToken)
    {
        var row = await _db.TierFeatureMaps.FirstOrDefaultAsync(
            m => m.Tier == tier && m.FeatureKey == metric,
            cancellationToken);

        if (row == null)
        {
            _db.TierFeatureMaps.Add(new TierFeatureMap { Tier = tier, FeatureKey = metric, CapValue = cap });
            return;
        }

        row.CapValue = cap;
    }

    private async Task<Dictionary<string, int?>> LoadCapsSnapshotAsync(string tier, CancellationToken cancellationToken)
    {
        var rows = await _db.TierFeatureMaps.AsNoTracking()
            .Where(m => m.Tier == tier && UsageMetrics.All.Contains(m.FeatureKey))
            .ToListAsync(cancellationToken);

        return UsageMetrics.All.ToDictionary(
            m => m,
            m => rows.FirstOrDefault(r => r.FeatureKey == m)?.CapValue);
    }

    private async Task<List<string>> LoadEnabledModuleFeaturesAsync(string tier, CancellationToken cancellationToken) =>
        await _db.TierFeatureMaps.AsNoTracking()
            .Where(m => m.Tier == tier && ModuleFeatureKeys.Contains(m.FeatureKey))
            .Select(m => m.FeatureKey)
            .OrderBy(k => k)
            .ToListAsync(cancellationToken);

    private async Task LogFieldChangesAsync(
        string tier,
        Guid actorId,
        string reason,
        object before,
        object after,
        CancellationToken cancellationToken)
    {
        foreach (var (field, oldValue, newValue) in DiffSnapshots(before, after))
        {
            await AppendChangeLogAsync(tier, field, oldValue, newValue, actorId, reason, cancellationToken);
        }
    }

    private Task AppendChangeLogAsync(
        string tier,
        string fieldName,
        object? oldValue,
        object? newValue,
        Guid actorId,
        string reason,
        CancellationToken cancellationToken)
    {
        _db.Set<PlanChangeLog>().Add(new PlanChangeLog
        {
            Tier = tier,
            FieldName = fieldName,
            OldValue = oldValue?.ToString(),
            NewValue = newValue?.ToString(),
            ActorPlatformUserId = actorId,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    private static IEnumerable<(string Field, string? OldValue, string? NewValue)> DiffSnapshots(object before, object after)
    {
        var beforeProps = before.GetType().GetProperties();
        var afterProps = after.GetType().GetProperties().ToDictionary(p => p.Name);

        foreach (var prop in beforeProps)
        {
            if (!afterProps.TryGetValue(prop.Name, out var afterProp))
                continue;

            var oldVal = prop.GetValue(before)?.ToString();
            var newVal = afterProp.GetValue(after)?.ToString();
            if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
                yield return (prop.Name, oldVal, newVal);
        }
    }

    private static object SnapshotMetadata(CommercialPlan plan) => new
    {
        plan.DisplayName,
        plan.Description,
        plan.SortOrder
    };

    private static string NormalizeTier(string tier) => tier.Trim().ToLowerInvariant();

    private static string? ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            return "A reason of at least 10 characters is required for commercial changes.";
        return null;
    }
}
