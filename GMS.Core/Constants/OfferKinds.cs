namespace GMS.Core.Constants;

public static class OfferAppliesTo
{
    public const string Memberships = "memberships";
    public const string Products = "products";
    public const string Both = "both";

    public static readonly string[] All = { Memberships, Products, Both };
}

public static class OfferDiscountTypes
{
    public const string Percentage = "percentage";
    public const string Fixed = "fixed";
    public const string Bxgy = "bxgy";

    public static readonly string[] All = { Percentage, Fixed, Bxgy };
}

public static class OfferRedemptions
{
    public const string Automatic = "automatic";
    public const string PromoCode = "promoCode";

    public static readonly string[] All = { Automatic, PromoCode };
}

public static class OfferStatuses
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string Expired = "expired";
}
