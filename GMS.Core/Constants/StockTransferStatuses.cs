namespace GMS.Core.Constants;

public static class StockTransferStatuses
{
    public const string Pending = "pending";
    public const string InTransit = "in_transit";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Pending, InTransit, Completed, Cancelled
    };
}
