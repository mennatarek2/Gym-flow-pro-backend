namespace GMS.Core.Constants;

/// <summary>Machine-readable rejection reasons returned by ISaleService, encoded as the leading
/// "CODE|message" segment of a failed Result's Error string.</summary>
public static class SaleFailureReasons
{
    public const string StaffUserNotFound = "STAFF_USER_NOT_FOUND";
    public const string MemberNotFound = "MEMBER_NOT_FOUND";
    public const string MemberCreateFailed = "MEMBER_CREATE_FAILED";
    public const string PlanNotFound = "PLAN_NOT_FOUND";
    public const string ForbiddenDiscountOverride = "FORBIDDEN_DISCOUNT_OVERRIDE";
    public const string Overpay = "OVERPAY";
    public const string PaymentIncomplete = "PAYMENT_INCOMPLETE";
    public const string OpenShiftRequired = "OPEN_SHIFT_REQUIRED";
    public const string PromoRaceLost = "PROMO_RACE_LOST";
    public const string SaleNotFound = "SALE_NOT_FOUND";
    public const string SaleNotCollectable = "SALE_NOT_COLLECTABLE";
    public const string PaymentExceedsAmountDue = "PAYMENT_EXCEEDS_AMOUNT_DUE";
    public const string InsufficientCredit = "INSUFFICIENT_CREDIT";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    /// <summary>Physical stock exists but none is sellable (e.g. all batches expired).</summary>
    public const string StockUnsellableExpired = "STOCK_UNSELLABLE_EXPIRED";
    public const string InventoryRequired = "INVENTORY_REQUIRED";
    public const string WarehouseNotFound = "WAREHOUSE_NOT_FOUND";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string MemberRequired = "MEMBER_REQUIRED";
}
