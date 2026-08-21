namespace GMS.Application.DTOs.Admin;

public class RoleCatalogDto
{
    public List<RoleAccessDto> Roles { get; set; } = new();
    public List<string> Universe { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public string EffectCopy { get; set; } = string.Empty;
}

public class RoleAccessDto
{
    public string Id { get; set; } = string.Empty;
    public bool Editable { get; set; }
    public bool IsCustomized { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<string> Defaults { get; set; } = new();
}

public class UpdateRolePermissionsRequest
{
    public List<string> Permissions { get; set; } = new();
}
