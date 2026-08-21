namespace GMS.Core.Constants;

public static class PurchaseOrderStatuses
{
    public const string Draft = "draft";
    public const string Approved = "approved";
    public const string PartiallyReceived = "partially_received";
    public const string Received = "received";
    public const string Cancelled = "cancelled";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Draft, Approved, PartiallyReceived, Received, Cancelled
    };

    public static readonly HashSet<string> Receivable = new(StringComparer.OrdinalIgnoreCase)
    {
        Approved, PartiallyReceived
    };
}

public static class SupplierLedgerReasons
{
    public const string Purchase = "purchase";
    public const string Payment = "payment";
    public const string ReturnCredit = "return_credit";
    /// <summary>Opening balance. Positive = owed to supplier (له); negative = owed by supplier (عليه).</summary>
    public const string Opening = "opening";
}
