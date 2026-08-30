namespace GMS.Core.Constants;

/// <summary>
/// Fine-grained permission identifiers used by the policy-based authorization system.
/// Embedded in JWTs as "perm" claims and matched against dynamically-created authorization policies.
/// </summary>
public static class Permissions
{
    /// <summary>Claim type used to carry a single permission string in the JWT.</summary>
    public const string ClaimType = "perm";

    public const string MembersView = "members.view";
    public const string MembersCreate = "members.create";
    public const string MembersEdit = "members.edit";

    public const string CheckinManual = "checkin.manual";
    public const string ClassesView = "classes.view";
    public const string AttendanceView = "attendance.view";

    public const string SalesSell = "sales.sell";
    public const string SalesDiscountApply = "sales.discount.apply";
    public const string SalesDiscountOverride = "sales.discount.override";

    public const string PaymentsCashAccept = "payments.cash.accept";
    public const string PaymentsRefundRequest = "payments.refund.request";
    public const string PaymentsRefundApprove = "payments.refund.approve";

    public const string ShiftOpen = "shift.open";
    public const string ShiftClose = "shift.close";
    public const string ShiftReconcileApprove = "shift.reconcile.approve";

    public const string MembershipsFreeze = "memberships.freeze";
    public const string PlansManage = "plans.manage";

    public const string ReportsFinancialView = "reports.financial.view";
    public const string ReportsExpensesView = "reports.expenses.view";
    public const string ReportsExpensesManage = "reports.expenses.manage";
    public const string SettingsManage = "settings.manage";

    public const string InventoryView = "inventory.view";
    public const string InventoryManage = "inventory.manage";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryPurchase = "inventory.purchase";
    public const string InventoryTransfer = "inventory.transfer";

    /// <summary>List/open member-store order inbox (desk).</summary>
    public const string MemberOrdersView = "member_orders.view";
    /// <summary>Accept / reject / ready / complete member-store orders.</summary>
    public const string MemberOrdersManage = "member_orders.manage";

    /// <summary>View HR directory: employees, departments, positions, contracts.</summary>
    public const string HrView = "hr.view";
    /// <summary>Create/edit employees, departments, positions, contracts; terminate employees.</summary>
    public const string HrManage = "hr.manage";
    /// <summary>Create/edit shift templates and assign/remove employee schedules.</summary>
    public const string HrShiftsManage = "hr.shifts.manage";
    /// <summary>Check other employees in/out and correct attendance records.</summary>
    public const string HrAttendanceManage = "hr.attendance.manage";
    /// <summary>View other employees' schedules and attendance history.</summary>
    public const string HrAttendanceView = "hr.attendance.view";

    /// <summary>View other employees' leave requests and balances.</summary>
    public const string HrLeaveView = "hr.leave.view";
    /// <summary>Create leave requests on behalf of others, manage/adjust leave balances.</summary>
    public const string HrLeaveManage = "hr.leave.manage";
    /// <summary>Approve/reject leave requests.</summary>
    public const string HrLeaveApprove = "hr.leave.approve";

    /// <summary>View other employees' payroll (salary, adjustments, net pay).</summary>
    public const string HrPayrollView = "hr.payroll.view";
    /// <summary>Create payroll periods, calculate payroll, create adjustments.</summary>
    public const string HrPayrollManage = "hr.payroll.manage";
    /// <summary>Approve and close payroll periods.</summary>
    public const string HrPayrollApprove = "hr.payroll.approve";

    /// <summary>View other employees' documents.</summary>
    public const string HrDocumentsView = "hr.documents.view";
    /// <summary>Upload/delete employee documents.</summary>
    public const string HrDocumentsManage = "hr.documents.manage";

    /// <summary>All known permission strings — the universe used to build the Owner grant and to validate policy names.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        MembersView, MembersCreate, MembersEdit,
        CheckinManual, ClassesView, AttendanceView,
        SalesSell, SalesDiscountApply, SalesDiscountOverride,
        PaymentsCashAccept, PaymentsRefundRequest, PaymentsRefundApprove,
        ShiftOpen, ShiftClose, ShiftReconcileApprove,
        MembershipsFreeze, PlansManage,
        ReportsFinancialView, ReportsExpensesView, ReportsExpensesManage, SettingsManage,
        InventoryView, InventoryManage, InventoryAdjust, InventoryPurchase, InventoryTransfer,
        MemberOrdersView, MemberOrdersManage,
        HrView, HrManage, HrShiftsManage, HrAttendanceManage, HrAttendanceView,
        HrLeaveView, HrLeaveManage, HrLeaveApprove,
        HrPayrollView, HrPayrollManage, HrPayrollApprove,
        HrDocumentsView, HrDocumentsManage
    };
}
