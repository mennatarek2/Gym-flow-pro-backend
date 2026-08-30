namespace GMS.Application.Services;

using System.Text.Json;
using System.Text.Json.Nodes;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Applies a tenant <c>role_permissions</c> overlay on top of
/// <see cref="IPermissionProvider"/> defaults. Owner and Member stay locked.
/// <see cref="ApplicationUser.PermissionsOverride"/> is not used.
/// </summary>
public static class RolePermissionResolver
{
    public static readonly string[] StaffRoles = { "Owner", "Manager", "Receptionist", "Trainer" };
    public static readonly string[] EditableRoles = { "Manager", "Receptionist", "Trainer" };

    private static readonly HashSet<string> KnownPerms = new(Permissions.All, StringComparer.Ordinal);
    private static readonly HashSet<string> Editable = new(EditableRoles, StringComparer.OrdinalIgnoreCase);

    public static string CanonicalRole(string? role)
    {
        var raw = (role ?? string.Empty).Trim();
        if (raw.Equals("Owner", StringComparison.OrdinalIgnoreCase)) return "Owner";
        if (raw.Equals("Manager", StringComparison.OrdinalIgnoreCase)) return "Manager";
        if (raw.Equals("Trainer", StringComparison.OrdinalIgnoreCase)) return "Trainer";
        if (raw.Equals("Receptionist", StringComparison.OrdinalIgnoreCase)) return "Receptionist";
        if (raw.Equals("Member", StringComparison.OrdinalIgnoreCase)) return "Member";
        if (raw.Equals("Employee", StringComparison.OrdinalIgnoreCase)) return "Employee";
        return raw;
    }

    public static bool IsEditable(string? role)
        => Editable.Contains(CanonicalRole(role));

    public static IReadOnlyList<string> NormalizeKeys(IEnumerable<string>? keys)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        if (keys != null)
        {
            foreach (var k in keys)
            {
                if (!string.IsNullOrWhiteSpace(k) && KnownPerms.Contains(k.Trim()))
                    granted.Add(k.Trim());
            }
        }

        return Permissions.All.Where(granted.Contains).ToList();
    }

    /// <summary>
    /// Overlay map: canonical editable role → normalized permission keys.
    /// Missing key means “use DefaultPermissionProvider”.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string>> ParseOverlay(string? tenantSettingsJson)
    {
        var overlay = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(tenantSettingsJson))
            return overlay;

        try
        {
            using var doc = JsonDocument.Parse(tenantSettingsJson);
            if (!doc.RootElement.TryGetProperty(TenantSettingsKeys.RolePermissions, out var node)
                || node.ValueKind != JsonValueKind.Object)
                return overlay;

            foreach (var prop in node.EnumerateObject())
            {
                var role = CanonicalRole(prop.Name);
                if (!IsEditable(role))
                    continue;
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    continue;

                var keys = prop.Value.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? string.Empty);
                overlay[role] = NormalizeKeys(keys);
            }
        }
        catch (JsonException)
        {
            return overlay;
        }

        return overlay;
    }

    public static IReadOnlySet<string> Resolve(
        IEnumerable<string> roles,
        IPermissionProvider provider,
        IReadOnlyDictionary<string, IReadOnlyList<string>> overlay)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in roles ?? Array.Empty<string>())
        {
            var role = CanonicalRole(raw);
            if (string.IsNullOrEmpty(role) || role == "Member" || role == "Employee")
                continue;

            if (role == "Owner")
            {
                result.UnionWith(provider.GetPermissions(new[] { "Owner" }));
                continue;
            }

            if (overlay.TryGetValue(role, out var custom))
                result.UnionWith(custom);
            else
                result.UnionWith(provider.GetPermissions(new[] { role }));
        }

        return result;
    }

    public static bool SameSet(IReadOnlyList<string> a, IReadOnlySet<string> b)
    {
        if (a.Count != b.Count) return false;
        return a.All(b.Contains);
    }

    public static string WriteOverlay(string? existingSettingsJson, Dictionary<string, IReadOnlyList<string>> overlay)
    {
        var settingsNode = string.IsNullOrWhiteSpace(existingSettingsJson)
            ? new JsonObject()
            : (JsonNode.Parse(existingSettingsJson) as JsonObject) ?? new JsonObject();

        if (overlay.Count == 0)
        {
            settingsNode.Remove(TenantSettingsKeys.RolePermissions);
        }
        else
        {
            var obj = new JsonObject();
            foreach (var role in EditableRoles)
            {
                if (!overlay.TryGetValue(role, out var keys))
                    continue;
                var arr = new JsonArray();
                foreach (var k in keys)
                    arr.Add(k);
                obj[role] = arr;
            }

            if (obj.Count == 0)
                settingsNode.Remove(TenantSettingsKeys.RolePermissions);
            else
                settingsNode[TenantSettingsKeys.RolePermissions] = obj;
        }

        return settingsNode.ToJsonString();
    }
}
