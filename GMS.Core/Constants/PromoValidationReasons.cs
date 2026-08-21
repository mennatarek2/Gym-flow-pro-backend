namespace GMS.Core.Constants;

/// <summary>
/// Machine-readable rejection reasons returned by <see cref="Interfaces.IPromoService"/>'s
/// promo validation. Each maps to exactly one business rule.
/// </summary>
public static class PromoValidationReasons
{
    public const string CodeNotFound = "CODE_NOT_FOUND";
    public const string CodeInactive = "CODE_INACTIVE";
    public const string DateRangeInvalid = "DATE_RANGE_INVALID";
    public const string MaxUsesReached = "MAX_USES_REACHED";
    public const string MemberMaxUsesReached = "MEMBER_MAX_USES_REACHED";
    public const string PlanNotInScope = "PLAN_NOT_IN_SCOPE";
    public const string BelowMinPrice = "BELOW_MIN_PRICE";

    /// <summary>Not part of the original spec's rejection list, but required for correctness — the
    /// plan referenced by ValidateAndPriceAsync must exist to compute a price at all.</summary>
    public const string PlanNotFound = "PLAN_NOT_FOUND";
}
