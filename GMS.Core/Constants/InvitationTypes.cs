namespace GMS.Core.Constants;

/// <summary>
/// Invitation type on member_invitations.
/// Product writes <see cref="Invitation"/> only. guest_pass / referral remain for historical rows.
/// </summary>
public static class InvitationTypes
{
    public const string Invitation = "invitation";
    public const string GuestPass = "guest_pass";
    public const string Referral = "referral";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Invitation,
        GuestPass,
        Referral
    };
}

/// <summary>Staff follow-up statuses for the Invitation product. Trial is not a status.</summary>
public static class InvitationStatuses
{
    public const string New = "new";
    public const string Contacted = "contacted";
    public const string Interested = "interested";
    public const string NotInterested = "not_interested";
    public const string Converted = "converted";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        New, Contacted, Interested, NotInterested, Converted
    };

    public static bool IsValid(string? raw)
        => !string.IsNullOrWhiteSpace(raw) && All.Contains(raw.Trim());
}

/// <summary>Plan-level default reward for successful referrals.</summary>
public static class ReferralRewardTypes
{
    public const string Credit = "credit";
    public const string FreeDays = "free_days";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Credit,
        FreeDays
    };

    public static bool IsValidOrEmpty(string? raw)
        => string.IsNullOrWhiteSpace(raw) || All.Contains(raw.Trim());
}
