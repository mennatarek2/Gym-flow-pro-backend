namespace GMS.Core.Constants;

/// <summary>Machine-readable rejection reasons returned by IShiftService, encoded as the leading
/// "CODE|message" segment of a failed Result's Error string (same convention as SaleFailureReasons).</summary>
public static class ShiftFailureReasons
{
    public const string StaffUserNotFound = "STAFF_USER_NOT_FOUND";
    public const string ShiftAlreadyOpen = "SHIFT_ALREADY_OPEN";
    public const string NoOpenShift = "NO_OPEN_SHIFT";
    public const string ShiftNotFound = "SHIFT_NOT_FOUND";
    public const string NotAwaitingApproval = "NOT_AWAITING_APPROVAL";
    public const string ManagerApprovalRequired = "MANAGER_APPROVAL_REQUIRED";
    public const string InvalidMovementType = "INVALID_MOVEMENT_TYPE";
    public const string ShiftNotOpen = "SHIFT_NOT_OPEN";
}
