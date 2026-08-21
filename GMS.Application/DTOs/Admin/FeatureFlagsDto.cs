namespace GMS.Application.DTOs.Admin;

/// <summary>
/// Documents the shape of the "feature_flags" object nested inside Tenant.Settings JSON
/// (see <see cref="GMS.Core.Utilities.FeatureFlagReader"/>, which is what
/// <c>GMS.Api.Filters.FeatureFlagFilter</c> actually reads at request time). All flags default to
/// true when absent — a tenant with no explicit configuration has every module enabled.
/// </summary>
public class FeatureFlagsDto
{
    public bool Sales { get; set; } = true;
    public bool Shifts { get; set; } = true;
    public bool Trials { get; set; } = true;
    public bool Refunds { get; set; } = true;
    public bool Debtors { get; set; } = true;
    public bool Imports { get; set; } = true;
    public bool Inventory { get; set; } = true;
}
