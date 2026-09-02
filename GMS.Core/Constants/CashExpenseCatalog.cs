namespace GMS.Core.Constants;

/// <summary>
/// Structured running-cost categories for CashExpense (not payroll, not supplier purchases).
/// </summary>
public static class CashExpenseCatalog
{
    public static readonly IReadOnlyList<string> Categories = new[]
    {
        "Utilities",
        "Rent & Property",
        "Software & Technology",
        "Operations",
        "Marketing",
        "Banking & Payment",
        "Other"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> TypesByCategory =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Utilities"] = new[] { "Electricity", "Water", "Gas", "Internet", "Telephone" },
            ["Rent & Property"] = new[] { "Rent", "Property services", "Maintenance" },
            ["Software & Technology"] = new[]
            {
                "Gym management software",
                "POS / software subscriptions",
                "Other SaaS subscriptions"
            },
            ["Operations"] = new[] { "Cleaning", "Security", "Repairs", "Supplies", "Equipment maintenance" },
            ["Marketing"] = new[] { "Advertising", "Social media", "Printing", "Promotions" },
            ["Banking & Payment"] = new[] { "Bank fees", "Payment gateway fees" },
            ["Other"] = new[] { "Miscellaneous" }
        };

    public static bool IsKnownCategory(string? category) =>
        !string.IsNullOrWhiteSpace(category)
        && Categories.Any(item => string.Equals(item, category.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownType(string? category, string? expenseType)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(expenseType))
            return false;
        var key = Categories.FirstOrDefault(item =>
            string.Equals(item, category.Trim(), StringComparison.OrdinalIgnoreCase));
        if (key == null)
            return false;
        var types = TypesByCategory[key];
        return types.Any(item => string.Equals(item, expenseType.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>SourceType for desk-posted running costs (not payroll_payment).</summary>
    public const string ManualRunningCostSourceType = "running_cost";
}
