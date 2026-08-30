namespace GMS.Core.Constants;

/// <summary>Staff in-app notification categories, priorities, and typed event keys.</summary>
public static class StaffNotificationCategories
{
    public const string Members = "Members";
    public const string Memberships = "Memberships";
    public const string Leads = "Leads";
    public const string Payments = "Payments";
    public const string Classes = "Classes";
    public const string Bookings = "Bookings";
    public const string Attendance = "Attendance";
    public const string Pos = "POS";
    public const string Inventory = "Inventory";
    public const string Purchasing = "Purchasing";
    public const string Staff = "Staff";
    public const string Shifts = "Shifts";
    public const string Security = "Security";
    public const string System = "System";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Members, Memberships, Leads, Payments, Classes, Bookings, Attendance,
        Pos, Inventory, Purchasing, Staff, Shifts, Security, System
    };
}

public static class StaffNotificationPriorities
{
    public const string Critical = "Critical";
    public const string ActionRequired = "ActionRequired";
    public const string Info = "Info";

    public static readonly IReadOnlyList<string> All = new[] { Critical, ActionRequired, Info };
}

public static class StaffNotificationTypes
{
    public const string LeadNew = "lead.new";
    public const string FollowUpDue = "lead.followup_due";
    public const string FollowUpOverdue = "lead.followup_overdue";
    public const string TrialEnding = "membership.trial_ending";
    public const string MembershipExpiring = "membership.expiring";
    public const string MembershipExpired = "membership.expired";
    public const string PaymentFailed = "payment.failed";
    public const string InvoiceOverdue = "payment.invoice_overdue";
    public const string RefundIssued = "payment.refund_issued";
    public const string BookingNew = "booking.new";
    public const string BookingCancelled = "booking.cancelled";
    public const string ClassFull = "class.full";
    public const string TrainerMissing = "class.trainer_missing";
    public const string ExpiredMembershipCheckin = "attendance.expired_membership_checkin";
    public const string LowStock = "inventory.low_stock";
    public const string OutOfStock = "inventory.out_of_stock";
    public const string PoRequiresApproval = "purchasing.po_requires_approval";
    public const string CashVariance = "shifts.cash_variance";
    public const string EmployeeActivated = "staff.employee_activated";
    public const string SecurityAlert = "security.alert";
}
