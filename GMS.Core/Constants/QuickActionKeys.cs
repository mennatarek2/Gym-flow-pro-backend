namespace GMS.Core.Constants;

/// <summary>
/// Dashboard Quick Action tile keys. Must match Frontend whitelist (quick-actions / dashboard).
/// Persist keys only — never routes, URLs, or labels.
/// </summary>
public static class QuickActionKeys
{
    public const int MaxTiles = 6;
    public const string ValidationError = "INVALID_QUICK_ACTIONS";

    public const string NewMember = "new_member";
    public const string Checkin = "checkin";
    public const string NewSale = "new_sale";
    public const string CollectPayment = "collect_payment";
    public const string NewTrial = "new_trial";
    public const string SendDebtorReminder = "send_debtor_reminder";
    public const string OpenShift = "open_shift";
    public const string CloseShift = "close_shift";
    public const string NewRefund = "new_refund";
    public const string AddPromoCode = "add_promo_code";
    public const string FreezeMembership = "freeze_membership";
    public const string BookClass = "book_class";
    public const string ViewClasses = "view_classes";
    public const string CheckinMember = "checkin_member";

    /// <summary>Legacy / alias from FE; coerce to <see cref="AddPromoCode"/>.</summary>
    public const string AliasNewOffer = "new_offer";

    public static readonly string[] DefaultKeys =
    {
        NewMember,
        Checkin,
        NewSale,
        CollectPayment
    };

    public static readonly HashSet<string> Whitelist = new(StringComparer.Ordinal)
    {
        NewMember,
        Checkin,
        NewSale,
        CollectPayment,
        NewTrial,
        SendDebtorReminder,
        OpenShift,
        CloseShift,
        NewRefund,
        AddPromoCode,
        FreezeMembership,
        BookClass,
        ViewClasses,
        CheckinMember
    };

    /// <summary>
    /// Incoming count &gt; <see cref="MaxTiles"/> → tooMany (400).
    /// Otherwise: alias, drop unknown, de-dupe first-seen order (ordinal).
    /// </summary>
    public static (bool TooMany, List<string> Keys) Normalize(IReadOnlyList<string>? incoming)
    {
        var raw = incoming ?? Array.Empty<string>();
        if (raw.Count > MaxTiles)
            return (true, raw.ToList());

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var item in raw)
        {
            var key = Coerce(item);
            if (key == null || !Whitelist.Contains(key) || !seen.Add(key))
                continue;
            keys.Add(key);
        }

        return (false, keys);
    }

    private static string? Coerce(string? item)
    {
        if (string.IsNullOrWhiteSpace(item))
            return null;
        var t = item.Trim();
        if (t == AliasNewOffer)
            return AddPromoCode;
        return t;
    }
}
