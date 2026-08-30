namespace GMS.Tests;

using GMS.Core.Constants;
using GMS.Infrastructure.Services;

public class DefaultPermissionProviderTests
{
    private readonly DefaultPermissionProvider _sut = new();

    [Fact]
    public void Owner_GetsEveryPermission()
    {
        var permissions = _sut.GetPermissions(new[] { "Owner" });

        Assert.Equal(Permissions.All.Count, permissions.Count);
        foreach (var permission in Permissions.All)
            Assert.Contains(permission, permissions);
    }

    [Fact]
    public void Manager_GetsEverythingExceptPlansAndSettings()
    {
        var permissions = _sut.GetPermissions(new[] { "Manager" });

        Assert.DoesNotContain(Permissions.PlansManage, permissions);
        Assert.DoesNotContain(Permissions.SettingsManage, permissions);
        Assert.Contains(Permissions.MembershipsFreeze, permissions);
        Assert.Contains(Permissions.ReportsFinancialView, permissions);
        Assert.Equal(Permissions.All.Count - 2, permissions.Count);
    }

    [Fact]
    public void Receptionist_GetsFrontDeskPermissionsOnly()
    {
        var permissions = _sut.GetPermissions(new[] { "Receptionist" });

        Assert.Contains(Permissions.MembersView, permissions);
        Assert.Contains(Permissions.MembersCreate, permissions);
        Assert.Contains(Permissions.MembersEdit, permissions);
        Assert.Contains(Permissions.CheckinManual, permissions);
        Assert.Contains(Permissions.SalesSell, permissions);
        Assert.Contains(Permissions.SalesDiscountApply, permissions);
        Assert.Contains(Permissions.PaymentsCashAccept, permissions);
        Assert.Contains(Permissions.PaymentsRefundRequest, permissions);
        Assert.Contains(Permissions.ShiftOpen, permissions);
        Assert.Contains(Permissions.ShiftClose, permissions);
        Assert.Contains(Permissions.InventoryView, permissions);
        Assert.Contains(Permissions.MemberOrdersView, permissions);
        Assert.Contains(Permissions.MemberOrdersManage, permissions);

        // Explicitly withheld from receptionist per spec
        Assert.DoesNotContain(Permissions.InventoryManage, permissions);
        Assert.DoesNotContain(Permissions.InventoryAdjust, permissions);
        Assert.DoesNotContain(Permissions.InventoryPurchase, permissions);
        Assert.DoesNotContain(Permissions.InventoryTransfer, permissions);
        Assert.DoesNotContain(Permissions.MembershipsFreeze, permissions);
        Assert.DoesNotContain(Permissions.PlansManage, permissions);
        Assert.DoesNotContain(Permissions.ReportsFinancialView, permissions);
        Assert.DoesNotContain(Permissions.SettingsManage, permissions);
        Assert.DoesNotContain(Permissions.SalesDiscountOverride, permissions);
        Assert.DoesNotContain(Permissions.PaymentsRefundApprove, permissions);
        Assert.DoesNotContain(Permissions.ShiftReconcileApprove, permissions);
    }

    [Fact]
    public void Trainer_GetsClassAttendanceAndManualCheckin()
    {
        var permissions = _sut.GetPermissions(new[] { "Trainer" });

        Assert.Equal(3, permissions.Count);
        Assert.Contains(Permissions.CheckinManual, permissions);
        Assert.Contains(Permissions.ClassesView, permissions);
        Assert.Contains(Permissions.AttendanceView, permissions);
    }

    [Fact]
    public void UnknownRole_GrantsNoPermissions()
    {
        var permissions = _sut.GetPermissions(new[] { "SomeUnknownRole" });

        Assert.Empty(permissions);
    }

    [Fact]
    public void MultipleRoles_UnionsPermissions()
    {
        var permissions = _sut.GetPermissions(new[] { "Trainer", "Receptionist" });

        Assert.Contains(Permissions.CheckinManual, permissions);
        Assert.Contains(Permissions.MembersView, permissions);
    }
}
