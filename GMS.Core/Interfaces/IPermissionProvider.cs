namespace GMS.Core.Interfaces;

/// <summary>
/// Resolves the effective permission set for a user given their role(s).
/// Default implementation is a hard-coded role→permission map; a future implementation
/// can layer per-user overrides on top without changing callers.
/// </summary>
public interface IPermissionProvider
{
    /// <summary>
    /// Returns the union of permissions granted by <paramref name="roles"/>.
    /// </summary>
    /// <param name="roles">Identity role names assigned to the user (e.g. "Owner", "Receptionist").</param>
    /// <param name="permissionsOverrideJson">
    /// Raw JSON from <c>AspNetUsers.PermissionsOverride</c>. Reserved for future per-user grants/revocations —
    /// currently parsed for forward compatibility but not applied.
    /// </param>
    IReadOnlySet<string> GetPermissions(IEnumerable<string> roles, string? permissionsOverrideJson = null);
}
