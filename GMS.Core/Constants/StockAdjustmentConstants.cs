namespace GMS.Core.Constants;

public static class StockAdjustmentStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Draft, Posted, Cancelled
    };
}

/// <summary>G4 structured Fix reason codes — ops labels; free-text note complements, never replaces.</summary>
public static class StockAdjustmentReasonCodes
{
    public const string Opening = "opening";
    public const string Damage = "damage";
    public const string Lost = "lost";
    public const string Expired = "expired";
    public const string ManualCount = "manual_count";
    public const string InternalUse = "internal_use";
    public const string Employee = "employee";
    public const string SupplierCorrection = "supplier_correction";
    public const string Other = "other";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Opening, Damage, Lost, Expired, ManualCount, InternalUse, Employee, SupplierCorrection, Other
    };

    /// <summary>Always decreases sellable/physical stock (write-off / consumption).</summary>
    public static bool RequiresDecrease(string reasonCode) =>
        reasonCode is Damage or Lost or Expired or InternalUse or Employee;

    /// <summary>Opening can only increase.</summary>
    public static bool RequiresIncrease(string reasonCode) =>
        reasonCode is Opening;

    public static bool RequiresNote(string reasonCode) =>
        reasonCode is Other;

    /// <summary>Expired write-offs must name a batch (when product tracks expiry).</summary>
    public static bool RequiresBatch(string reasonCode) =>
        reasonCode is Expired;
}
