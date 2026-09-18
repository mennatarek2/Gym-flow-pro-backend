namespace GMS.Core.Utilities;

/// <summary>
/// Member-desk outstanding is money remaining, not a sale-status name.
/// Collectable = AmountDue &gt; 0 and not refunded/cancelled/written_off/partially_refunded.
/// </summary>
public static class SaleCollectability
{
    public static readonly string[] ClosedStatuses =
    [
        "refunded",
        "cancelled",
        "written_off",
        "partially_refunded",
    ];

    public static bool IsClosedStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && ClosedStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool HasCollectableBalance(decimal amountDue, string? status) =>
        amountDue > 0m && !IsClosedStatus(status);
}
