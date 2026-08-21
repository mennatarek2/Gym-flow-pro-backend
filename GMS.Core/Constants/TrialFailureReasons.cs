namespace GMS.Core.Constants;

/// <summary>
/// Machine-readable rejection reasons returned by ITrialService, encoded as the leading
/// "CODE|message" segment of Result.Error (see SaleFailureReasons/ShiftFailureReasons for the
/// same convention).
/// </summary>
public static class TrialFailureReasons
{
    public const string PlanNotFound = "PLAN_NOT_FOUND";
    public const string PlanNotTrial = "PLAN_NOT_TRIAL";
    public const string TrialAlreadyUsed = "TRIAL_ALREADY_USED";
    public const string OtpInvalid = "OTP_INVALID";
    public const string PendingTrialNotFound = "PENDING_TRIAL_NOT_FOUND";
    public const string StaffUserNotFound = "STAFF_USER_NOT_FOUND";
}
