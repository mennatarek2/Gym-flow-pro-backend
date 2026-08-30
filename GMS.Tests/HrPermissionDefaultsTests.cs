namespace GMS.Tests;

using GMS.Core.Constants;
using GMS.Infrastructure.Services;

/// <summary>
/// Owner/Manager get full HR access automatically (Manager inherits any new entry in
/// Permissions.All unless explicitly excluded); Receptionist/Trainer are denied by default
/// and only gain access if the tenant explicitly grants it via the Roles overlay.
/// </summary>
public class HrPermissionDefaultsTests
{
    private readonly DefaultPermissionProvider _sut = new();

    [Fact]
    public void Owner_HasFullHrAccess()
    {
        var permissions = _sut.GetPermissions(new[] { "Owner" });

        Assert.Contains(Permissions.HrView, permissions);
        Assert.Contains(Permissions.HrManage, permissions);
    }

    [Fact]
    public void Manager_HasFullHrAccessByDefault()
    {
        var permissions = _sut.GetPermissions(new[] { "Manager" });

        Assert.Contains(Permissions.HrView, permissions);
        Assert.Contains(Permissions.HrManage, permissions);
    }

    [Fact]
    public void Receptionist_CanViewDirectoryAndManageAttendanceButNotEditDirectoryOrShifts()
    {
        // Phase 3: front desk checks employees in/out (needs HrView to know who's who) but cannot
        // edit the employee directory or redesign shift templates.
        var permissions = _sut.GetPermissions(new[] { "Receptionist" });

        Assert.Contains(Permissions.HrView, permissions);
        Assert.Contains(Permissions.HrAttendanceManage, permissions);
        Assert.Contains(Permissions.HrAttendanceView, permissions);
        Assert.DoesNotContain(Permissions.HrManage, permissions);
        Assert.DoesNotContain(Permissions.HrShiftsManage, permissions);
    }

    [Fact]
    public void Trainer_HasNoHrAccessByDefault()
    {
        var permissions = _sut.GetPermissions(new[] { "Trainer" });

        Assert.DoesNotContain(Permissions.HrView, permissions);
        Assert.DoesNotContain(Permissions.HrManage, permissions);
    }
}
