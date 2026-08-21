namespace GMS.Core.Constants;

/// <summary>Status vocabulary for referral_rewards.</summary>
public static class ReferralRewardStatuses
{
    public const string PendingHold = "pending_hold";
    public const string Granted = "granted";
    public const string Reversed = "reversed";
    public const string Forfeited = "forfeited";
}

/// <summary>Who receives a dual-sided referral reward row.</summary>
public static class ReferralRewardRoles
{
    public const string Referrer = "referrer";
    public const string Referee = "referee";
}

/// <summary>MemberCredit.EntryType for referral grants/reversals (signed amount).</summary>
public static class MemberCreditEntryTypes
{
    public const string Refund = "refund";
    public const string PaymentUse = "payment_use";
    public const string Adjustment = "adjustment";
    public const string ReferralReward = "referral_reward";
}

/// <summary>Ambassador / tier badges (no points economy).</summary>
public static class ReferralTiers
{
    public const string None = "none";
    public const string Bronze = "bronze";
    public const string Silver = "silver";
    public const string Gold = "gold";
    public const string Ambassador = "ambassador";

    public static string FromSuccessfulCount(int count) => count switch
    {
        >= 50 => Ambassador,
        >= 30 => Gold,
        >= 15 => Silver,
        >= 5 => Bronze,
        _ => None
    };
}
