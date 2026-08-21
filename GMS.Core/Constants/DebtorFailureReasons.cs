namespace GMS.Core.Constants;

/// <summary>Machine-readable rejection reasons returned by IDebtorsService, encoded as the leading
/// "CODE|message" segment of a failed Result's Error string (same convention as SaleFailureReasons).</summary>
public static class DebtorFailureReasons
{
    public const string MemberNotFound = "MEMBER_NOT_FOUND";
    public const string NoOutstandingBalance = "NO_OUTSTANDING_BALANCE";
    public const string ReminderThrottle = "REMINDER_THROTTLE";
}
