namespace GMS.Core.Constants;

/// <summary>Stock movement reason codes (INVS-3+).</summary>
public static class StockMovementReasons
{
    public const string Opening = "opening";
    public const string PurchaseReceipt = "purchase_receipt";
    public const string PurchaseReturn = "purchase_return";
    public const string Sale = "sale";
    public const string SaleRefund = "sale_refund";
    public const string Adjustment = "adjustment";
    public const string TransferOut = "transfer_out";
    public const string TransferIn = "transfer_in";
    public const string Count = "count";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Opening, PurchaseReceipt, PurchaseReturn, Sale, SaleRefund,
        Adjustment, TransferOut, TransferIn, Count
    };
}

/// <summary>Common ReferenceType values for idempotent stock posts.</summary>
public static class StockReferenceTypes
{
    public const string SaleLine = "SaleLine";
    public const string Refund = "Refund";
    public const string StockAdjustment = "StockAdjustment";
    public const string GoodsReceiptLine = "GoodsReceiptLine";
    public const string StockTransferLine = "StockTransferLine";
    /// <summary>Compensating return-to-source on in-transit reject (INVS-8). ReferenceId = StockTransferLine.Id.</summary>
    public const string StockTransferRejectLine = "StockTransferRejectLine";
    public const string StockCount = "StockCount";
    /// <summary>Stock restore for a retail SaleLine on refund approve (INVS-7). ReferenceId = SaleLine.Id.</summary>
    public const string RefundSaleLine = "RefundSaleLine";
}
