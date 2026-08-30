namespace GMS.Platform.Persistence;

using GMS.Platform.Constants;
using GMS.Platform.Entities;

/// <summary>
/// Hard-coded tier → feature / cap seed for <c>platform.tier_feature_map</c>.
/// Not Stage-2 API-editable — change here + migration/seed only.
/// </summary>
public static class TierFeatureMapSeed
{
    public static IReadOnlyList<TierFeatureMap> BuildAll()
    {
        var rows = new List<TierFeatureMap>();

        // --- starter ---
        // Inventory without stock_management → Products desk only (Add stock / Fix qty).
        // Refunds included: retail POS must be able to reverse a cash sale.
        AddModules(rows, PlanTiers.Starter, FeatureKeys.Sales, FeatureKeys.Shifts, FeatureKeys.Trials, FeatureKeys.Debtors,
            FeatureKeys.Refunds, FeatureKeys.Inventory, FeatureKeys.Hr);
        AddCap(rows, PlanTiers.Starter, UsageMetrics.ActiveMembers, 200);
        AddCap(rows, PlanTiers.Starter, UsageMetrics.StaffSeats, 3);
        AddCap(rows, PlanTiers.Starter, UsageMetrics.Branches, 1);
        AddCap(rows, PlanTiers.Starter, UsageMetrics.WhatsAppMessages, 500);

        // --- growth ---
        AddModules(rows, PlanTiers.Growth,
            FeatureKeys.Sales, FeatureKeys.Shifts, FeatureKeys.Trials, FeatureKeys.Debtors,
            FeatureKeys.Refunds, FeatureKeys.Imports, FeatureKeys.Inventory, FeatureKeys.Hr);
        AddCap(rows, PlanTiers.Growth, UsageMetrics.ActiveMembers, 1000);
        AddCap(rows, PlanTiers.Growth, UsageMetrics.StaffSeats, 10);
        AddCap(rows, PlanTiers.Growth, UsageMetrics.Branches, 3);
        AddCap(rows, PlanTiers.Growth, UsageMetrics.WhatsAppMessages, 2000);

        // --- pro ---
        AddModules(rows, PlanTiers.Pro, FeatureKeys.PhaseAModules);
        AddCap(rows, PlanTiers.Pro, UsageMetrics.ActiveMembers, 5000);
        AddCap(rows, PlanTiers.Pro, UsageMetrics.StaffSeats, 25);
        AddCap(rows, PlanTiers.Pro, UsageMetrics.Branches, 10);
        AddCap(rows, PlanTiers.Pro, UsageMetrics.WhatsAppMessages, 10000);

        // --- enterprise (null CapValue = unlimited) ---
        AddModules(rows, PlanTiers.Enterprise, FeatureKeys.PhaseAModules);
        AddCap(rows, PlanTiers.Enterprise, UsageMetrics.ActiveMembers, null);
        AddCap(rows, PlanTiers.Enterprise, UsageMetrics.StaffSeats, null);
        AddCap(rows, PlanTiers.Enterprise, UsageMetrics.Branches, null);
        AddCap(rows, PlanTiers.Enterprise, UsageMetrics.WhatsAppMessages, null);

        return rows;
    }

    private static void AddModules(List<TierFeatureMap> rows, string tier, params string[] keys)
    {
        foreach (var key in keys)
        {
            rows.Add(new TierFeatureMap { Tier = tier, FeatureKey = key, CapValue = null });
        }
    }

    private static void AddCap(List<TierFeatureMap> rows, string tier, string metric, int? cap) =>
        rows.Add(new TierFeatureMap { Tier = tier, FeatureKey = metric, CapValue = cap });
}
