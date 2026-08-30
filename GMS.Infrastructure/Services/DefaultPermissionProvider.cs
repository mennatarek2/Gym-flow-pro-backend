namespace GMS.Infrastructure.Services;

using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Hard-coded role→permission defaults. Tenant overlay lives in Tenant.Settings
/// (<c>role_permissions</c>) and is applied by <c>RolePermissionResolver</c> at login/refresh.
/// <c>ApplicationUser.PermissionsOverride</c> remains unused.
/// </summary>
public class DefaultPermissionProvider : IPermissionProvider
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolePermissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Owner"] = new HashSet<string>(Permissions.All),

            // Everything except plan and tenant-settings management.
            ["Manager"] = new HashSet<string>(Permissions.All.Except(new[]
            {
                Permissions.PlansManage,
                Permissions.SettingsManage
            })),

            ["Receptionist"] = new HashSet<string>(new[]
            {
                Permissions.MembersView, Permissions.MembersCreate, Permissions.MembersEdit,
                Permissions.CheckinManual, Permissions.ClassesView, Permissions.AttendanceView,
                Permissions.SalesSell, Permissions.SalesDiscountApply,
                Permissions.PaymentsCashAccept, Permissions.PaymentsRefundRequest,
                Permissions.ShiftOpen, Permissions.ShiftClose,
                Permissions.InventoryView,
                Permissions.MemberOrdersView, Permissions.MemberOrdersManage,
                // Front desk checks employees in/out and can see who's who, but cannot edit the
                // directory or redesign shift templates.
                Permissions.HrView, Permissions.HrAttendanceManage, Permissions.HrAttendanceView
            }),

            // Trainers can read classes, rosters and attendance without receiving the
            // full Members module or any financial permissions.
            ["Trainer"] = new HashSet<string>
            {
                Permissions.CheckinManual, Permissions.ClassesView, Permissions.AttendanceView
            }
        };

    public IReadOnlySet<string> GetPermissions(IEnumerable<string> roles, string? permissionsOverrideJson = null)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in roles)
        {
            if (RolePermissions.TryGetValue(role, out var perms))
                result.UnionWith(perms);
        }

        // permissionsOverrideJson intentionally ignored for now — see IPermissionProvider remarks.
        return result;
    }
}
