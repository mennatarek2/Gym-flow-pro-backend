namespace GMS.Core.Constants;

/// <summary>Machine-readable rejection reasons returned by ICallSheetService, encoded as the leading
/// "CODE|message" segment of a failed Result's Error string (same convention as SaleFailureReasons).</summary>
public static class CallSheetFailureReasons
{
    public const string MembershipNotFound = "MEMBERSHIP_NOT_FOUND";
    public const string StaffUserNotFound = "STAFF_USER_NOT_FOUND";
    public const string InvalidOutcome = "INVALID_OUTCOME";
    public const string FollowUpNotFound = "FOLLOW_UP_NOT_FOUND";
    public const string MemberNotFound = "MEMBER_NOT_FOUND";
    public const string InvalidReason = "INVALID_REASON";
    public const string InvalidStatus = "INVALID_STATUS";
    public const string InvalidPriority = "INVALID_PRIORITY";
    public const string InvalidNextAction = "INVALID_NEXT_ACTION";
}
