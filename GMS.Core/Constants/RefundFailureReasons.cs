namespace GMS.Core.Constants;

/// <summary>Machine-readable rejection reasons returned by IRefundService, encoded as the leading
/// "CODE|message" segment of a failed Result's Error string (same convention as SaleFailureReasons).</summary>
public static class RefundFailureReasons
{
    public const string StaffUserNotFound = "STAFF_USER_NOT_FOUND";
    public const string SaleNotFound = "SALE_NOT_FOUND";
    public const string RefundNotFound = "REFUND_NOT_FOUND";
    public const string RefundExceedsRemainder = "REFUND_EXCEEDS_REMAINDER";
    public const string SaleFullyRefunded = "SALE_FULLY_REFUNDED";
    public const string NotAwaitingApproval = "NOT_AWAITING_APPROVAL";
    public const string SelfApprovalForbidden = "SELF_APPROVAL_FORBIDDEN";
    public const string OpenShiftRequired = "OPEN_SHIFT_REQUIRED";
    public const string GatewayRefundUnsupported = "GATEWAY_REFUND_UNSUPPORTED";
    public const string InsufficientCredit = "INSUFFICIENT_CREDIT";
    public const string OriginalSaleMovementMissing = "ORIGINAL_SALE_MOVEMENT_MISSING";
    public const string StockRestoreFailed = "STOCK_RESTORE_FAILED";
}
