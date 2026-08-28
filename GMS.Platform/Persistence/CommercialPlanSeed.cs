namespace GMS.Platform.Persistence;

using GMS.Platform.Constants;
using GMS.Platform.Entities;

public static class CommercialPlanSeed
{
    public static IReadOnlyList<CommercialPlan> BuildAll() =>
    [
        new()
        {
            Tier = PlanTiers.Starter,
            DisplayName = "Starter",
            Description = "Small gyms getting started.",
            SortOrder = 1,
            IsActiveForSales = true,
            IsDefault = false,
            MonthlyPriceEgp = 999m,
            UpdatedAtUtc = DateTime.UtcNow
        },
        new()
        {
            Tier = PlanTiers.Growth,
            DisplayName = "Growth",
            Description = "Mid-market gyms with imports and HR.",
            SortOrder = 2,
            IsActiveForSales = true,
            IsDefault = true,
            MonthlyPriceEgp = 1999m,
            UpdatedAtUtc = DateTime.UtcNow
        },
        new()
        {
            Tier = PlanTiers.Pro,
            DisplayName = "Pro",
            Description = "Multi-branch operators with full inventory.",
            SortOrder = 3,
            IsActiveForSales = true,
            IsDefault = false,
            MonthlyPriceEgp = 3999m,
            UpdatedAtUtc = DateTime.UtcNow
        },
        new()
        {
            Tier = PlanTiers.Enterprise,
            DisplayName = "Enterprise",
            Description = "Unlimited scale and full module access.",
            SortOrder = 4,
            IsActiveForSales = true,
            IsDefault = false,
            MonthlyPriceEgp = 7999m,
            UpdatedAtUtc = DateTime.UtcNow
        }
    ];
}
