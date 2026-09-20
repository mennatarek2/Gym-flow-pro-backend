namespace GMS.Core.Constants;

/// <summary>Employee lifecycle status. Kept intentionally small — see HR module product boundary.</summary>
public static class EmployeeStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Terminated = "Terminated";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Active, Suspended, Terminated
    };
}

/// <summary>Employment contract type.</summary>
public static class EmploymentTypes
{
    public const string FullTime = "FullTime";
    public const string PartTime = "PartTime";
    public const string Temporary = "Temporary";
    public const string Contract = "Contract";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        FullTime, PartTime, Temporary, Contract
    };
}

/// <summary>Employment contract lifecycle status.</summary>
public static class ContractStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Ended = "Ended";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Draft, Active, Ended
    };
}

/// <summary>Employee attendance status for a single day. Present/Late are computed automatically
/// at check-in; Absent/HalfDay/OnLeave are only ever set via a manager correction (Phase 3 has no
/// automatic end-of-day absence job).</summary>
public static class AttendanceStatuses
{
    public const string Present = "Present";
    public const string Late = "Late";
    public const string Absent = "Absent";
    public const string HalfDay = "HalfDay";
    public const string OnLeave = "OnLeave";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Present, Late, Absent, HalfDay, OnLeave
    };
}

/// <summary>Where an attendance check-in/out originated. Device is set by the biometric
/// reconciliation foundation when a validated push event is auto-applied. System is used only for
/// OnLeave placeholder rows created by approved-leave/attendance integration — never by a
/// person checking in/out.</summary>
public static class AttendanceSources
{
    public const string Manual = "Manual";
    public const string Reception = "Reception";
    public const string Employee = "Employee";
    public const string Device = "Device";
    public const string System = "System";

    /// <summary>Self check-in via the gym's short-lived QR display, confirmed by the employee (see
    /// EmployeeAttendanceController's qr-validate/qr-check-in). Distinct from <see cref="Employee"/>
    /// (a plain no-QR self check-in tap) purely for observability/reporting.</summary>
    public const string Qr = "Qr";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Manual, Reception, Employee, Device, System, Qr
    };
}

/// <summary>How a biometric device (or bridge) is expected to talk to HyMotion Local.</summary>
public static class BiometricIntegrationTypes
{
    /// <summary>Preferred foundation target: device/bridge POSTs attendance events to Local.</summary>
    public const string PushToLocal = "PushToLocal";
    public const string LanPull = "LanPull";
    public const string VendorMiddleware = "VendorMiddleware";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        PushToLocal, LanPull, VendorMiddleware
    };
}

/// <summary>Honest device health — never implies an unverified hardware handshake succeeded.</summary>
public static class BiometricDeviceHealthStatuses
{
    public const string AwaitingEvents = "AwaitingEvents";
    public const string Receiving = "Receiving";
    public const string Disabled = "Disabled";
    public const string Error = "Error";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        AwaitingEvents, Receiving, Disabled, Error
    };
}

public static class BiometricRemoteDisableStatuses
{
    public const string NotSupported = "NotSupported";
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Failed = "Failed";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        NotSupported, Pending, Confirmed, Failed
    };
}

public static class BiometricPunchDirections
{
    public const string Unknown = "Unknown";
    public const string CheckIn = "CheckIn";
    public const string CheckOut = "CheckOut";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Unknown, CheckIn, CheckOut
    };
}

public static class BiometricEventStatuses
{
    public const string Pending = "Pending";
    public const string Applied = "Applied";
    public const string Duplicate = "Duplicate";
    public const string NeedsReview = "NeedsReview";
    public const string Rejected = "Rejected";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Pending, Applied, Duplicate, NeedsReview, Rejected
    };
}

/// <summary>Leave category. Kept deliberately small — no legal/labor-law automation.</summary>
public static class LeaveTypes
{
    public const string Annual = "Annual";
    public const string Sick = "Sick";
    public const string Emergency = "Emergency";
    public const string Unpaid = "Unpaid";
    public const string Permission = "Permission";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Annual, Sick, Emergency, Unpaid, Permission
    };

    /// <summary>Unpaid leave never tracks/consumes a balance — everything else does.</summary>
    public static bool TracksBalance(string leaveType) => !string.Equals(leaveType, Unpaid, StringComparison.OrdinalIgnoreCase);

    /// <summary>Practical, tenant-editable starting point (see LeaveBalanceService.SetEntitlementAsync
    /// to override per employee/year). Not Egyptian labor law — a simple, configurable default.</summary>
    public static decimal DefaultEntitlementDays(string leaveType) => leaveType switch
    {
        Annual => 21m,
        Sick => 14m,
        Emergency => 6m,
        Permission => 3m,
        _ => 0m
    };
}

/// <summary>Leave request lifecycle.</summary>
public static class LeaveRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Pending, Approved, Rejected, Cancelled
    };
}

/// <summary>Payroll period lifecycle. Approved/Closed periods are frozen — see PayrollPeriodService.</summary>
public static class PayrollPeriodStatuses
{
    public const string Draft = "Draft";
    public const string Calculated = "Calculated";
    public const string Approved = "Approved";
    public const string Closed = "Closed";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Draft, Calculated, Approved, Closed
    };
}

/// <summary>Manual payroll adjustment types. Overtime here adds to (does not replace) the amount
/// auto-derived from EmployeeAttendance.OvertimeMinutes.</summary>
public static class PayrollAdjustmentTypes
{
    public const string Bonus = "Bonus";
    public const string Allowance = "Allowance";
    public const string Overtime = "Overtime";
    public const string Deduction = "Deduction";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Bonus, Allowance, Overtime, Deduction
    };
}

/// <summary>Employee document category.</summary>
public static class EmployeeDocumentTypes
{
    public const string NationalId = "NationalId";
    public const string Contract = "Contract";
    public const string Certificate = "Certificate";
    public const string TrainingCertificate = "TrainingCertificate";
    public const string Other = "Other";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        NationalId, Contract, Certificate, TrainingCertificate, Other
    };
}

/// <summary>Derived (not stored) expiry status for an employee document, computed against Cairo today.</summary>
public static class DocumentExpiryStatuses
{
    public const string Valid = "Valid";
    public const string ExpiringSoon = "ExpiringSoon";
    public const string Expired = "Expired";
}
