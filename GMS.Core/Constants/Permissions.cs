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

    /// <summary>All known permission strings — the universe used to build the Owner grant and to validate policy names.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        MembersView, MembersCreate, MembersEdit,
        CheckinManual,
        SalesSell, SalesDiscountApply, SalesDiscountOverride,
        PaymentsCashAccept, PaymentsRefundRequest, PaymentsRefundApprove,
        ShiftOpen, ShiftClose, ShiftReconcileApprove,
        MembershipsFreeze, PlansManage,
        ReportsFinancialView, SettingsManage,
        InventoryView, InventoryManage, InventoryAdjust, InventoryPurchase, InventoryTransfer,
        MemberOrdersView, MemberOrdersManage
    };
}
